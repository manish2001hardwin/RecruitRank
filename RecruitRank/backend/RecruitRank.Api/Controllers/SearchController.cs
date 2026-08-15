using Microsoft.AspNetCore.Mvc;
using RecruitRank.Api.Models;
using RecruitRank.Api.Services;
using System.IO.Compression;

namespace RecruitRank.Api.Controllers;

[ApiController]
[Route("api")]
public class SearchController : ControllerBase
{
    private readonly JdParser _jdParser;
    private readonly PythonServiceClient _pythonClient;
    private readonly MatchEngine _matchEngine;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        JdParser jdParser,
        PythonServiceClient pythonClient,
        MatchEngine matchEngine,
        IWebHostEnvironment env,
        ILogger<SearchController> logger)
    {
        _jdParser = jdParser;
        _pythonClient = pythonClient;
        _matchEngine = matchEngine;
        _env = env;
        _logger = logger;
    }

    [HttpPost("search")]
    [RequestSizeLimit(200_000_000)] // 200 MB, generous for a batch of resumes/zips
    public async Task<ActionResult<SearchResponse>> Search([FromForm] string jdText, [FromForm] List<IFormFile> files)
    {
        if (string.IsNullOrWhiteSpace(jdText))
            return BadRequest("Job description text is required.");
        if (files == null || files.Count == 0)
            return BadRequest("At least one resume file is required.");

        // Each request gets its own temp folder so cleanup is a single directory delete.
        var requestId = Guid.NewGuid().ToString("N");
        var tempDir = Path.Combine(Path.GetTempPath(), "recruitrank", requestId);
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePaths = await SaveUploadedFilesAsync(files, tempDir);
            if (filePaths.Count == 0)
                return BadRequest("No valid PDF/DOCX files found (check ZIP contents or file types).");

            var jd = _jdParser.Parse(jdText);
            var processResult = await _pythonClient.ProcessResumesAsync(filePaths);

            List<RankedCandidate> ranked = new();
            if (processResult.Candidates.Count > 0)
            {
                var jdEmbedding = await _pythonClient.EmbedJdAsync(jd.Summary);
                ranked = _matchEngine.Rank(jd, processResult.Candidates, jdEmbedding);
            }

            return Ok(new SearchResponse { Ranked = ranked, Failed = processResult.Failed });
        }
        finally
        {
            // Fix: always clean up temp files, success or failure, so they
            // don't accumulate on disk across requests.
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up temp dir {Dir}", tempDir); }
        }
    }

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx",
    };

    private async Task<List<string>> SaveUploadedFilesAsync(List<IFormFile> files, string tempDir)
    {
        var savedPaths = new List<string>();

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.FileName);

            if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                var zipPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.zip");
                await using (var stream = System.IO.File.Create(zipPath))
                    await file.CopyToAsync(stream);

                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    var entryExt = Path.GetExtension(entry.Name);
                    if (!AllowedExtensions.Contains(entryExt) || string.IsNullOrEmpty(entry.Name))
                        continue;
                    var extractPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}{entryExt}");
                    entry.ExtractToFile(extractPath);
                    savedPaths.Add(extractPath);
                }
            }
            else if (AllowedExtensions.Contains(ext))
            {
                var savePath = Path.Combine(tempDir, $"{Guid.NewGuid():N}{ext}");
                await using var stream = System.IO.File.Create(savePath);
                await file.CopyToAsync(stream);
                savedPaths.Add(savePath);
            }
            // silently skip unsupported file types (e.g. .txt, .jpg) rather than failing the whole request
        }

        return savedPaths;
    }
}

using System.Net.Http.Json;
using RecruitRank.Api.Models;

namespace RecruitRank.Api.Services;

public class PythonServiceClient
{
    private readonly HttpClient _http;

    public PythonServiceClient(HttpClient http)
    {
        // base address configured in Program.cs from appsettings ("PythonService:BaseUrl")
        _http = http;
    }

    public async Task<PythonProcessResponse> ProcessResumesAsync(List<string> filePaths)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/process", new { file_paths = filePaths });
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<PythonProcessResponse>();
            return result ?? new PythonProcessResponse();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Python service unreachable/timed out -> surface as "all files failed"
            // instead of crashing the whole /api/search request.
            return new PythonProcessResponse
            {
                Failed = filePaths.Select(f => new ProcessedFailure
                {
                    File = f,
                    Reason = $"AI service unavailable: {ex.Message}",
                }).ToList(),
            };
        }
    }

    public async Task<float[]> EmbedJdAsync(string summary)
    {
        var response = await _http.PostAsJsonAsync("/embed_jd", new { summary });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EmbedResponse>();
        return result?.Embedding ?? Array.Empty<float>();
    }

    private class EmbedResponse
    {
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}

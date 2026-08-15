using System.Text.Json.Serialization;

namespace RecruitRank.Api.Models;

public class JdRequirements
{
    public string Title { get; set; } = "";
    public List<string> RequiredSkills { get; set; } = new();
    public List<string> NiceToHaveSkills { get; set; } = new();
    public int? MinExperience { get; set; }
    public int? MaxExperience { get; set; }
    public string? Location { get; set; }
    public bool StrictLocation { get; set; } = false;
    public string Summary { get; set; } = "";
}

public class CandidateProfile
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("skills")] public List<string> Skills { get; set; } = new();
    [JsonPropertyName("total_experience")] public double TotalExperience { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("current_title")] public string? CurrentTitle { get; set; }
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("source_file")] public string SourceFile { get; set; } = "";
    [JsonPropertyName("embedding")] public float[] Embedding { get; set; } = Array.Empty<float>();
    public bool WillingToRelocate { get; set; } = true;

    // populated during ranking
    [JsonIgnore] public double SemanticScore { get; set; }
    [JsonIgnore] public Evidence? Evidence { get; set; }
}

public class ProcessedFailure
{
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}

public class PythonProcessResponse
{
    [JsonPropertyName("candidates")] public List<CandidateProfile> Candidates { get; set; } = new();
    [JsonPropertyName("failed")] public List<ProcessedFailure> Failed { get; set; } = new();
}

public class Evidence
{
    public List<string> MatchedSkills { get; set; } = new();
    public List<string> MissingSkills { get; set; } = new();
    public string ExperienceVerdict { get; set; } = "";
    public string LocationStatus { get; set; } = "";
    public double OverallScore { get; set; }
}

public class RankedCandidate
{
    public CandidateProfile Candidate { get; set; } = null!;
    public Evidence Evidence { get; set; } = null!;
}

public class SearchResponse
{
    public List<RankedCandidate> Ranked { get; set; } = new();
    public List<ProcessedFailure> Failed { get; set; } = new();
}

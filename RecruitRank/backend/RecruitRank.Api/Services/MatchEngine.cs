using RecruitRank.Api.Models;

namespace RecruitRank.Api.Services;

public class MatchEngine
{
    public List<RankedCandidate> Rank(JdRequirements jd, List<CandidateProfile> candidates, float[] jdEmbedding)
    {
        // 1. Hard filters — mandatory skills, min experience, (optional) strict location
        var eligible = candidates.Where(c =>
            jd.RequiredSkills.All(s => c.Skills.Contains(s, StringComparer.OrdinalIgnoreCase)) &&
            (!jd.MinExperience.HasValue || c.TotalExperience >= jd.MinExperience.Value) &&
            (!jd.StrictLocation || string.Equals(c.Location, jd.Location, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        // 2. Semantic score (cosine similarity against JD embedding)
        foreach (var c in eligible)
            c.SemanticScore = CosineSimilarity(jdEmbedding, c.Embedding);

        // 3. Deterministic tie-break chain
        var ranked = eligible
            .OrderByDescending(c => c.SemanticScore)
            .ThenByDescending(c => MandatorySkillCoverage(c, jd.RequiredSkills))
            .ThenByDescending(c => ExperienceRelevance(c.TotalExperience, jd.MinExperience, jd.MaxExperience))
            .ThenByDescending(c => TitleOverlap(c.CurrentTitle, jd.Title))
            .ToList();

        // 4. Evidence per candidate
        var result = new List<RankedCandidate>();
        foreach (var c in ranked)
        {
            var evidence = GenerateEvidence(c, jd);
            c.Evidence = evidence;
            result.Add(new RankedCandidate { Candidate = c, Evidence = evidence });
        }
        return result;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0.0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0.0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static double MandatorySkillCoverage(CandidateProfile c, List<string> requiredSkills)
    {
        if (requiredSkills.Count == 0) return 1.0;
        var matched = requiredSkills.Count(s => c.Skills.Contains(s, StringComparer.OrdinalIgnoreCase));
        return (double)matched / requiredSkills.Count;
    }

    /// <summary>
    /// Gaussian curve centered at the ideal experience (midpoint of min/max).
    /// sigma is derived from the JD's own range so the curve's "tolerance"
    /// scales with how wide a band the recruiter specified. A fixed
    /// fallback (3 years) is used when no max is given.
    /// </summary>
    private static double ExperienceRelevance(double candidateExp, int? minExp, int? maxExp)
    {
        if (!minExp.HasValue && !maxExp.HasValue) return 1.0;

        double min = minExp ?? 0;
        double max = maxExp ?? (min + 6); // assume a 6-year band if no cap given
        double ideal = (min + max) / 2.0;
        double sigma = maxExp.HasValue ? Math.Max((max - min) / 2.0, 1.0) : 3.0;

        double diff = candidateExp - ideal;
        return Math.Exp(-(diff * diff) / (2 * sigma * sigma));
    }

    private static double TitleOverlap(string? candidateTitle, string jdTitle)
    {
        if (string.IsNullOrWhiteSpace(candidateTitle) || string.IsNullOrWhiteSpace(jdTitle)) return 0.0;
        var a = new HashSet<string>(candidateTitle.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var b = new HashSet<string>(jdTitle.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (a.Count == 0 || b.Count == 0) return 0.0;
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static Evidence GenerateEvidence(CandidateProfile c, JdRequirements jd)
    {
        var matched = jd.RequiredSkills.Where(s => c.Skills.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
        var missing = jd.RequiredSkills.Except(matched, StringComparer.OrdinalIgnoreCase).ToList();

        return new Evidence
        {
            MatchedSkills = matched,
            MissingSkills = missing,
            ExperienceVerdict = $"{c.TotalExperience} years (requirement: {jd.MinExperience?.ToString() ?? "0"}+ years)",
            LocationStatus = jd.StrictLocation
                ? (string.Equals(c.Location, jd.Location, StringComparison.OrdinalIgnoreCase) ? "Match" : "Mismatch")
                : $"{c.Location ?? "Unknown"}{(c.WillingToRelocate ? " (open to relocate)" : "")}",
            OverallScore = Math.Round(c.SemanticScore * 100, 1),
        };
    }
}

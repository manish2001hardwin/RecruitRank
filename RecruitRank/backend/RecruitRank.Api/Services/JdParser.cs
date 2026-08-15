using System.Text.Json;
using System.Text.RegularExpressions;
using RecruitRank.Api.Models;

namespace RecruitRank.Api.Services;

public class JdParser
{
    // canonical skill -> list of aliases, loaded from the SAME skills.json
    // the Python service uses, so both sides agree on what a skill "is".
    private readonly Dictionary<string, List<string>> _taxonomy;
    private readonly List<(string alias, string canonical)> _aliasToCanonical;

    public JdParser(IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "skills.json");
        var json = File.ReadAllText(path);
        _taxonomy = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
                    ?? new Dictionary<string, List<string>>();

        _aliasToCanonical = new List<(string, string)>();
        foreach (var (canonical, aliases) in _taxonomy)
            foreach (var alias in aliases)
                _aliasToCanonical.Add((alias.ToLowerInvariant(), canonical));

        // longest alias first, so "asp.net core" beats "asp.net"
        _aliasToCanonical = _aliasToCanonical.OrderByDescending(x => x.alias.Length).ToList();
    }

    public JdRequirements Parse(string jdText)
    {
        var lowerText = jdText.ToLowerInvariant();

        var mandatorySkills = ExtractSkills(GetMandatorySection(jdText));
        var allSkills = ExtractSkills(jdText);
        var niceToHave = allSkills.Except(mandatorySkills).ToList();
        // if we couldn't isolate a "must have" section, treat everything found as mandatory
        if (mandatorySkills.Count == 0)
        {
            mandatorySkills = allSkills;
            niceToHave = new List<string>();
        }

        var (minExp, maxExp) = ExtractExperience(jdText);
        var title = jdText.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        var location = ExtractLocation(jdText);

        var summary = $"Required Role: {title}. Skills: {string.Join(", ", mandatorySkills)}. " +
                       $"Experience: {minExp?.ToString() ?? "0"}-{maxExp?.ToString() ?? "any"} years.";

        return new JdRequirements
        {
            Title = title,
            RequiredSkills = mandatorySkills,
            NiceToHaveSkills = niceToHave,
            MinExperience = minExp,
            MaxExperience = maxExp,
            Location = location,
            StrictLocation = false, // recruiter can override in the UI
            Summary = summary,
        };
    }

    private static string GetMandatorySection(string text)
    {
        // crude but effective: grab the paragraph(s) after "must have" / "required skills"
        var match = Regex.Match(
            text,
            @"(must[\s-]?have|required\s+skills?|mandatory)[:\-]?\s*(.+?)(?:\n\s*\n|nice[\s-]?to[\s-]?have|preferred|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[2].Value : "";
    }

    private List<string> ExtractSkills(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        var lower = text.ToLowerInvariant();
        var found = new HashSet<string>();
        foreach (var (alias, canonical) in _aliasToCanonical)
        {
            var pattern = $@"(?<![a-zA-Z0-9]){Regex.Escape(alias)}(?![a-zA-Z0-9])";
            if (Regex.IsMatch(lower, pattern))
                found.Add(canonical);
        }
        return found.OrderBy(x => x).ToList();
    }

    private static (int? min, int? max) ExtractExperience(string text)
    {
        // "3-5 years", "3 to 5 years", "5+ years", "minimum 2 years"
        var range = Regex.Match(text, @"(\d+)\s*(?:-|to)\s*(\d+)\+?\s*years?", RegexOptions.IgnoreCase);
        if (range.Success)
            return (int.Parse(range.Groups[1].Value), int.Parse(range.Groups[2].Value));

        var plus = Regex.Match(text, @"(\d+)\+\s*years?", RegexOptions.IgnoreCase);
        if (plus.Success)
            return (int.Parse(plus.Groups[1].Value), null);

        var atLeast = Regex.Match(text, @"(?:minimum|at least)\s*(\d+)\s*years?", RegexOptions.IgnoreCase);
        if (atLeast.Success)
            return (int.Parse(atLeast.Groups[1].Value), null);

        return (null, null);
    }

    private static readonly string[] KnownCities =
    {
        "bengaluru", "bangalore", "mumbai", "delhi", "hyderabad", "pune", "chennai",
        "kolkata", "gurgaon", "gurugram", "noida", "ahmedabad", "remote",
    };

    private static string? ExtractLocation(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var city in KnownCities)
        {
            if (Regex.IsMatch(lower, $@"(?<![a-zA-Z]){city}(?![a-zA-Z])"))
                return char.ToUpper(city[0]) + city[1..];
        }
        return null;
    }
}

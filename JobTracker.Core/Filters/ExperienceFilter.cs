using System.Text.RegularExpressions;
using JobTracker.Core.Models;

namespace JobTracker.Core.Filters;

/// <summary>
/// Classifies a job posting into an <see cref="ExperienceLevel"/> based on the
/// description text and job title.
///
/// ── Classification strategy ─────────────────────────────────────────────────
/// 1. If the title indicates a management role → Manager
/// 2. Extract all year requirements from the description.
///    The highest value is used (handles "3 yrs React, 10 yrs overall").
///    Map to level: 0–2 → Entry · 3–7 → Mid · 8+ → Senior
/// 3. If no year value found, scan description + title for level keywords.
/// 4. If still unclear → Unknown (always shown for manual review).
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public partial class ExperienceFilter
{
    // ── Year-extraction regex patterns ────────────────────────────────────────

    [GeneratedRegex(
        @"(?:minimum\s+(?:of\s+)?|at\s+least\s+)?(?<![-–])(\d{1,2})\s*\+?\s*" +
        @"(?:or\s+more\s+)?years?(?:\s+of\s+[\w\s]{0,30}?experience)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumericYearPattern();

    [GeneratedRegex(
        @"(\d{1,2})\s*(?:[-–]|\s+to\s+)\s*\d{1,2}\s+years?(?:\s+of\s+[\w\s]{0,20}?experience)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex YearRangePattern();

    [GeneratedRegex(
        @"\b(one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen)" +
        @"\s*\+?\s*(?:or\s+more\s+)?years?(?:\s+of\s+[\w\s]{0,20}?experience)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex WrittenNumberYearPattern();

    private static readonly Dictionary<string, int> WrittenNumbers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["one"]=1,["two"]=2,["three"]=3,["four"]=4,["five"]=5,
            ["six"]=6,["seven"]=7,["eight"]=8,["nine"]=9,["ten"]=10,
            ["eleven"]=11,["twelve"]=12,["thirteen"]=13,["fourteen"]=14,["fifteen"]=15,
        };

    // ── Keyword signals ───────────────────────────────────────────────────────

    private static readonly string[] ManagerTitleKeywords =
    [
        "engineering manager", "software manager", "technology manager",
        "development manager", "technical manager", "technical program manager",
        "director of engineering", "director of software", "director of technology",
        "director of platform", "director of infrastructure",
        "vp engineering", "vp of engineering", "vp of technology", "vp of software",
        "head of engineering", "head of software", "head of technology",
        "chief technology officer", "cto",
        "software delivery manager", "devops manager", "platform manager",
        "delivery manager", "it manager",
    ];

    private static readonly string[] EntryKeywords =
        ["entry level", "entry-level", "junior", "new grad", "new graduate",
         "graduate engineer", "associate engineer", "intern"];

    private static readonly string[] MidKeywords =
        ["mid level", "mid-level", "intermediate", "associate software",
         "associate developer"];

    private static readonly string[] SeniorKeywords =
        ["senior", "sr.", "staff engineer", "principal", "lead engineer",
         "lead developer", "architect", " iii", " iv", " v", "distinguished"];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies the experience level of a job posting.
    /// </summary>
    /// <param name="description">Full job description text.</param>
    /// <param name="title">Job title.</param>
    public ExperienceLevel Classify(string description, string title)
    {
        // 1. Manager title takes priority.
        if (IsManagerTitle(title))
            return ExperienceLevel.Manager;

        // 2. Explicit year requirements in the description.
        var years = ExtractAllYears(description);
        if (years.Count > 0)
            return YearsToLevel(years.Max());

        // 3. Level keywords in description + title.
        var combined = $"{title} {description}".ToLowerInvariant();

        if (EntryKeywords.Any(k => combined.Contains(k, StringComparison.Ordinal)))
            return ExperienceLevel.Entry;

        if (SeniorKeywords.Any(k => combined.Contains(k, StringComparison.Ordinal)))
            return ExperienceLevel.Senior;

        if (MidKeywords.Any(k => combined.Contains(k, StringComparison.Ordinal)))
            return ExperienceLevel.Mid;

        return ExperienceLevel.Unknown;
    }

    /// <summary>
    /// Returns true if the title matches a known engineering management pattern.
    /// </summary>
    public static bool IsManagerTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var lower = title.Trim().ToLowerInvariant();
        return ManagerTitleKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static ExperienceLevel YearsToLevel(int years) => years switch
    {
        <= 2 => ExperienceLevel.Entry,
        <= 7 => ExperienceLevel.Mid,
        _    => ExperienceLevel.Senior,
    };

    private static List<int> ExtractAllYears(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return [];
        var results = new List<int>();

        foreach (Match m in NumericYearPattern().Matches(description))
            if (int.TryParse(m.Groups[1].Value, out var y) && y is >= 0 and <= 50)
                results.Add(y);

        foreach (Match m in YearRangePattern().Matches(description))
            if (int.TryParse(m.Groups[1].Value, out var y) && y is >= 0 and <= 50)
                results.Add(y);

        foreach (Match m in WrittenNumberYearPattern().Matches(description))
            if (WrittenNumbers.TryGetValue(m.Groups[1].Value, out var y))
                results.Add(y);

        return results;
    }
}

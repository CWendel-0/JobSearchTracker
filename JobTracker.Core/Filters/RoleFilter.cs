using JobTracker.Core.Models;

namespace JobTracker.Core.Filters;

/// <summary>
/// Determines whether a job title describes a qualifying technical role.
///
/// Rules applied in order:
///   1. Built-in leading-word exclusions (Manager, Director, VP …)
///      — bypassed for manager titles when <see cref="ExperienceLevel.Manager"/>
///        is in the enabled levels set.
///   2. User-configured extra exclusion keywords — hard stop.
///   3. User-configured extra inclusion keywords — qualifies.
///   4. Built-in manager-tech keywords (when Manager level enabled).
///   5. Built-in qualifying tech keywords.
///   6. No match — does not qualify.
/// </summary>
public class RoleFilter
{
    private static readonly string[] QualifyingKeywords =
    [
        "software engineer", "software developer", "software architect",
        "application developer", "application engineer",
        "full stack", "fullstack", "full-stack",
        "frontend engineer", "front-end engineer", "front end engineer",
        "backend engineer", "back-end engineer", "back end engineer",
        "devops engineer", "dev ops engineer",
        "site reliability engineer", "sre",
        "platform engineer", "data engineer", "cloud engineer",
        "infrastructure engineer", "systems engineer", "solutions architect",
        "ml engineer", "machine learning engineer",
    ];

    // Titles that BEGIN with these are excluded unless Manager level is enabled
    // and the title also matches a manager-tech keyword.
    private static readonly string[] LeadingExclusionPrefixes =
    [
        "manager", "director", "vp ", "vp,", "vice president",
        "head of", "chief ", "president", "recruiter",
        "hr ", "human resources",
    ];

    // Qualifying keywords for management roles (checked when Manager is enabled).
    private static readonly string[] ManagerKeywords =
    [
        "engineering manager", "software manager", "technology manager",
        "development manager", "technical manager", "technical program manager",
        "director of engineering", "director of software", "director of technology",
        "director of platform", "director of infrastructure",
        "vp engineering", "vp of engineering", "vp of technology", "vp of software",
        "head of engineering", "head of software", "head of technology",
        "chief technology officer", "cto",
        "software delivery manager", "devops manager", "platform manager",
        "delivery manager",
    ];

    private readonly IReadOnlyList<string>         _extraInclude;
    private readonly IReadOnlyList<string>         _extraExclude;
    private readonly IReadOnlySet<ExperienceLevel> _enabledLevels;

    public RoleFilter(
        IEnumerable<string>?         extraInclude   = null,
        IEnumerable<string>?         extraExclude   = null,
        IEnumerable<ExperienceLevel>? enabledLevels = null)
    {
        _extraInclude  = (extraInclude ?? [])
            .Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).ToList();
        _extraExclude  = (extraExclude ?? [])
            .Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).ToList();
        _enabledLevels = (enabledLevels ?? [ExperienceLevel.Senior]).ToHashSet();
    }

    /// <summary>
    /// Returns <c>true</c> if the title describes a qualifying technical
    /// or (when enabled) engineering management role.
    /// </summary>
    public bool Qualifies(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        var normalised   = title.Trim().ToLowerInvariant();
        var managerEnabled = _enabledLevels.Contains(ExperienceLevel.Manager);

        // 1. Leading exclusion prefixes
        var hasLeadingExclusion = LeadingExclusionPrefixes
            .Any(p => normalised.StartsWith(p, StringComparison.Ordinal));

        if (hasLeadingExclusion)
        {
            // When Manager is enabled, still allow through if it is a known
            // engineering management title.
            if (managerEnabled &&
                ManagerKeywords.Any(k => normalised.Contains(k, StringComparison.Ordinal)))
                return true;

            return false;
        }

        // 2. User exclusions
        foreach (var kw in _extraExclude)
            if (normalised.Contains(kw, StringComparison.Ordinal)) return false;

        // 3. User inclusions
        foreach (var kw in _extraInclude)
            if (normalised.Contains(kw, StringComparison.Ordinal)) return true;

        // 4. Manager-tech keywords (titles that don't start with a prefix,
        //    e.g. "Engineering Manager")
        if (managerEnabled &&
            ManagerKeywords.Any(k => normalised.Contains(k, StringComparison.Ordinal)))
            return true;

        // 5. Core qualifying keywords
        foreach (var kw in QualifyingKeywords)
            if (normalised.Contains(kw, StringComparison.Ordinal)) return true;

        return false;
    }
}

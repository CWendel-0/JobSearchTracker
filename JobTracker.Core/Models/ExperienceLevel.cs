namespace JobTracker.Core.Models;

/// <summary>
/// Classifies the experience level of a job posting based on description text
/// and title keywords. Set by <see cref="JobTracker.Core.Filters.ExperienceFilter"/>;
/// never set by fetchers.
/// </summary>
public enum ExperienceLevel
{
    /// <summary>
    /// 0–2 years stated explicitly, or entry/junior keywords present.
    /// </summary>
    Entry,

    /// <summary>
    /// 3–7 years stated explicitly, or mid-level keywords present.
    /// </summary>
    Mid,

    /// <summary>
    /// 8+ years stated explicitly, or senior/staff/principal keywords present.
    /// </summary>
    Senior,

    /// <summary>
    /// Title indicates an engineering management role
    /// (e.g. Engineering Manager, Director of Engineering).
    /// </summary>
    Manager,

    /// <summary>
    /// No clear experience signal found in the description or title.
    /// Postings at this level are always included and flagged with ⚠
    /// in the review table for manual evaluation.
    /// </summary>
    Unknown,
}

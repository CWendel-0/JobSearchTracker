namespace JobTracker.Core.Models;

/// <summary>
/// Represents a single job posting retrieved from any source, normalised into a
/// common schema. Fetchers produce these; the pipeline filters and deduplicates them.
/// </summary>
public record class JobPosting
{
    /// <summary>Name of the hiring company or agency.</summary>
    public string Company { get; init; } = string.Empty;

    /// <summary>The job title as listed in the posting.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Direct URL to the job posting.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Date the posting was published, as reported by the source.</summary>
    public DateOnly PostingDate { get; init; }

    /// <summary>Name of the job board this posting came from (e.g. "USAJOBS", "Remotive").</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Full description text. Used by ExperienceFilter for level classification.
    /// Not written to the spreadsheet.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Recruiter or contact email/name, if available. Empty string if not.</summary>
    public string Contact { get; init; } = string.Empty;

    /// <summary>
    /// Experience level assigned by the pipeline. Defaults to Unknown so that
    /// any posting not run through the filter is treated conservatively.
    /// </summary>
    public ExperienceLevel Level { get; set; } = ExperienceLevel.Unknown;

    /// <summary>
    /// Returns the three-field tuple used to detect duplicate postings.
    /// Comparison normalises case and whitespace.
    /// </summary>
    public (string Company, string Title, DateOnly Date) DeduplicationKey =>
    (
        Company.Trim().ToLowerInvariant(),
        Title.Trim().ToLowerInvariant(),
        PostingDate
    );
}

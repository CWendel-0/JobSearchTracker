using JobTracker.Core.Models;

namespace JobTracker.Core;

/// <summary>
/// The output of a <see cref="Pipeline.ProcessAsync"/> run. Contains the net-new
/// qualifying postings and counters that the UI displays in the status bar.
/// </summary>
public class PipelineResult
{
    /// <summary>
    /// Postings that passed the role filter, the experience filter, and the
    /// deduplication check. These are presented to the user for review before
    /// being written to the spreadsheet.
    /// </summary>
    public List<JobPosting> NewPostings { get; } = [];

    /// <summary>
    /// Number of postings whose title did not match any qualifying role keyword.
    /// </summary>
    public int RoleFiltered { get; set; }

    /// <summary>
    /// Number of postings whose description explicitly stated fewer than 8 years
    /// of experience required.
    /// </summary>
    public int ExperienceFiltered { get; set; }

    /// <summary>
    /// Number of postings discarded because their posting date was older than
    /// the configured <c>DaysBack</c> window.
    /// </summary>
    public int DateFiltered { get; set; }

    /// <summary>
    /// Number of postings that already existed in the spreadsheet (matched on
    /// Company + Title + Posting Date) and were therefore skipped.
    /// </summary>
    public int Duplicates { get; set; }

    /// <summary>
    /// Per-fetcher errors collected during a run. A failed fetcher is isolated
    /// and does not abort results from other sources.
    /// </summary>
    public List<FetcherError> FetcherErrors { get; } = [];

    /// <summary>Total postings that entered the pipeline before any filtering.</summary>
    public int TotalProcessed =>
        NewPostings.Count + RoleFiltered + ExperienceFiltered + DateFiltered + Duplicates;
}

/// <summary>Records that a particular fetcher failed during a run.</summary>
public class FetcherError
{
    public required string SourceName { get; init; }
    public required string Message    { get; init; }
    public Exception?      Exception  { get; init; }
}

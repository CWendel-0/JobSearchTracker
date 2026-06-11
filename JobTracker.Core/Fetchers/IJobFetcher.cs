using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Common interface for all job board fetchers. Each fetcher is responsible
/// for querying one source and returning a list of normalised <see cref="JobPosting"/>
/// objects. Filtering and deduplication happen downstream in the pipeline.
/// </summary>
public interface IJobFetcher
{
    /// <summary>
    /// Human-readable name of the source this fetcher targets (e.g. "USAJOBS").
    /// Written to the Source column of every posting this fetcher produces.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Fetches job postings from the source and returns them as a flat list.
    /// Implementations should handle paging internally and surface errors as
    /// exceptions; the pipeline catches and isolates per-fetcher failures.
    /// </summary>
    Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default);
}

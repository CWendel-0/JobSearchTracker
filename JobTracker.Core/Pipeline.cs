using JobTracker.Core.Fetchers;
using JobTracker.Core.Filters;
using JobTracker.Core.Models;

namespace JobTracker.Core;

/// <summary>
/// Orchestrates a job-fetch run:
///   1. Accepts raw postings (Phase 2) or calls fetchers in parallel (Phase 3+)
///   2. Applies the role filter — drops non-engineering titles
///   3. Applies the experience filter — drops sub-threshold postings, flags uncertain ones
///   4. Deduplicates against postings already in the spreadsheet
///   5. Returns a <see cref="PipelineResult"/> for the UI to review
///
/// Writing to the sheet is intentionally NOT done here. The UI presents results
/// first; the user confirms before <see cref="ISheetsClient.AppendRowsAsync"/>
/// is called.
/// </summary>
public class Pipeline
{
    private readonly ISheetsClient             _sheets;
    private readonly RoleFilter               _roleFilter;
    private readonly ExperienceFilter         _experienceFilter;
    private readonly int                      _daysBack;
    private readonly IReadOnlySet<ExperienceLevel> _enabledLevels;

    /// <param name="daysBack">
    /// Postings older than this many days are discarded. 0 disables the filter.
    /// </param>
    /// <param name="enabledLevels">
    /// Experience levels to include. Unknown is always included regardless.
    /// Defaults to Senior only.
    /// </param>
    public Pipeline(
        ISheetsClient                  sheetsClient,
        RoleFilter                     roleFilter,
        ExperienceFilter               experienceFilter,
        int                            daysBack      = 0,
        IEnumerable<ExperienceLevel>?  enabledLevels = null)
    {
        _sheets           = sheetsClient;
        _roleFilter       = roleFilter;
        _experienceFilter = experienceFilter;
        _daysBack         = daysBack;
        _enabledLevels    = (enabledLevels ?? [ExperienceLevel.Senior]).ToHashSet();
    }

    // ── Phase 2: process a pre-supplied list of postings ─────────────────────

    /// <summary>
    /// Filters and deduplicates <paramref name="rawPostings"/> against the live
    /// spreadsheet. Used directly in tests and will be called by
    /// <see cref="RunAsync"/> once fetchers are wired up in Phase 3.
    /// </summary>
    public async Task<PipelineResult> ProcessAsync(
        IEnumerable<JobPosting> rawPostings,
        CancellationToken ct = default)
    {
        var existingKeys = await _sheets.GetExistingKeysAsync(ct);
        var result       = new PipelineResult();

        foreach (var posting in rawPostings)
        {
            ct.ThrowIfCancellationRequested();

            // ── 0. Date filter ───────────────────────────────────────────────
            if (_daysBack > 0)
            {
                var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-_daysBack));
                if (posting.PostingDate < cutoff)
                {
                    result.DateFiltered++;
                    continue;
                }
            }

            // ── 1. Role filter ───────────────────────────────────────────────
            if (!_roleFilter.Qualifies(posting.Title))
            {
                result.RoleFiltered++;
                continue;
            }

            // ── 2. Experience filter ─────────────────────────────────────────
            var level = _experienceFilter.Classify(posting.Description, posting.Title);

            // Unknown level is always included (flagged for manual review).
            if (level != ExperienceLevel.Unknown &&
                !_enabledLevels.Contains(level))
            {
                result.ExperienceFiltered++;
                continue;
            }

            // ── 3. Deduplication ─────────────────────────────────────────────
            var key = posting.DeduplicationKey;
            if (existingKeys.Contains(key))
            {
                result.Duplicates++;
                continue;
            }

            // Track within-batch so a source returning the same posting twice
            // doesn't produce duplicate rows in a single run.
            existingKeys.Add(key);

            posting.Level = level;
            result.NewPostings.Add(posting);
        }

        return result;
    }

    // ── Phase 3+: run all enabled fetchers in parallel ───────────────────────

    /// <summary>
    /// Calls all <paramref name="fetchers"/> concurrently, collects their
    /// postings, and passes the combined list through
    /// <see cref="ProcessAsync"/>. A fetcher that throws is caught and recorded
    /// in <see cref="PipelineResult.FetcherErrors"/> so other sources still
    /// contribute results.
    ///
    /// This method is a stub in Phase 2 — the body is implemented in Phase 3
    /// once the first fetcher exists.
    /// </summary>
    public async Task<PipelineResult> RunAsync(
        IEnumerable<IJobFetcher> fetchers,
        CancellationToken ct = default)
    {
        var fetchTasks = fetchers
            .Select(f => FetchSafeAsync(f, ct))
            .ToList();

        var fetchResults = await Task.WhenAll(fetchTasks);

        // Flatten all postings from successful fetchers
        var allPostings = fetchResults.SelectMany(r => r.Postings).ToList();

        // Process the combined list
        var result = await ProcessAsync(allPostings, ct);

        // Attach any fetcher-level errors
        foreach (var fr in fetchResults.Where(r => r.Error is not null))
            result.FetcherErrors.Add(fr.Error!);

        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<FetchResult> FetchSafeAsync(
        IJobFetcher fetcher,
        CancellationToken ct)
    {
        try
        {
            var postings = await fetcher.FetchAsync(ct);
            return new FetchResult(postings, null);
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate normally
        }
        catch (Exception ex)
        {
            return new FetchResult(
                [],
                new FetcherError
                {
                    SourceName = fetcher.SourceName,
                    Message    = ex.Message,
                    Exception  = ex,
                });
        }
    }

    private record FetchResult(
        IReadOnlyList<JobPosting> Postings,
        FetcherError?             Error);
}

using JobTracker.Core.Models;

namespace JobTracker.Core;

/// <summary>
/// Abstracts Google Sheets access so the pipeline and UI can be unit-tested
/// without a live spreadsheet. <see cref="SheetsClient"/> is the production
/// implementation; tests use a Moq mock.
/// </summary>
public interface ISheetsClient
{
    /// <summary>
    /// Reads all existing rows from the Applications sheet and returns the set
    /// of deduplication keys already present. The pipeline uses this to decide
    /// which newly-fetched postings are genuinely new.
    /// </summary>
    Task<HashSet<(string Company, string Title, DateOnly Date)>> GetExistingKeysAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Appends one row per posting to the Applications sheet. Only columns
    /// written by the program are populated; checkbox and auto-date columns
    /// are left for the Apps Script trigger to manage.
    /// </summary>
    Task AppendRowsAsync(IEnumerable<JobPosting> postings, CancellationToken ct = default);
}

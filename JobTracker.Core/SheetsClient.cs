using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using JobTracker.Core.Models;

namespace JobTracker.Core;

/// <summary>
/// Reads from and writes to the Job Application Tracker Google Sheet using a
/// service account for authentication.
///
/// ── Sheet layout (columns A–L) ──────────────────────────────────────────────
///   A  Company Name        ← written by this program
///   B  Job Title           ← written by this program
///   C  Job Posting URL     ← written by this program
///   D  Posting Date        ← written by this program (ISO yyyy-MM-dd)
///   E  Source              ← written by this program
///   F  Applied?            ← checkbox; left FALSE by this program
///   G  Application Date    ← auto-filled by Apps Script trigger
///   H  Received Response?  ← checkbox; left FALSE by this program
///   I  Response Date       ← auto-filled by Apps Script trigger
///   J  Received Interview? ← checkbox; left FALSE by this program
///   K  Interview Date      ← auto-filled by Apps Script trigger
///   L  Contact Information ← written by this program
/// ────────────────────────────────────────────────────────────────────────────
/// </summary>
public class SheetsClient : ISheetsClient
{
    private const string SheetName    = "Applications";
    private const string ReadRange    = "Applications!A2:D";   // only need A-D for dedup keys
    private const string AppName      = "JobTracker";

    // Date formats to attempt when parsing the Posting Date column.
    // "MMM d, yyyy" matches the format set by the Apps Script ("May 15, 2026").
    private static readonly string[] DateFormats =
        ["MMM d, yyyy", "MMMM d, yyyy", "M/d/yyyy", "yyyy-MM-dd"];

    private readonly SheetsService _service;
    private readonly string _spreadsheetId;

    /// <param name="serviceAccountKeyPath">
    /// Absolute path to the Google service account JSON key file.
    /// </param>
    /// <param name="spreadsheetId">
    /// The ID from the spreadsheet URL:
    /// https://docs.google.com/spreadsheets/d/[SPREADSHEET_ID]/edit
    /// </param>
    public SheetsClient(string serviceAccountKeyPath, string spreadsheetId)
    {
        var json = File.ReadAllText(serviceAccountKeyPath);

        // TODO: Google.Apis.Auth has deprecated FromJson in favour of a
        // CredentialFactory pattern, but that API is not yet stable across
        // package versions. Suppress the warning until the replacement is
        // documented. Track: https://github.com/googleapis/google-api-dotnet-client
#pragma warning disable CS0618
        var credential = GoogleCredential
            .FromJson(json)
            .CreateScoped(SheetsService.Scope.Spreadsheets);
#pragma warning restore CS0618

        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = AppName,
        });

        _spreadsheetId = spreadsheetId;
    }

    /// <inheritdoc/>
    public async Task<HashSet<(string Company, string Title, DateOnly Date)>>
        GetExistingKeysAsync(CancellationToken ct = default)
    {
        var request = _service.Spreadsheets.Values.Get(_spreadsheetId, ReadRange);
        var response = await request.ExecuteAsync(ct);

        var keys = new HashSet<(string, string, DateOnly)>();

        if (response.Values is null)
            return keys;

        foreach (var row in response.Values)
        {
            // Skip rows that don't have at least columns A, B, and D.
            if (row.Count < 4) continue;

            var company  = row[0]?.ToString()?.Trim().ToLowerInvariant() ?? "";
            var title    = row[1]?.ToString()?.Trim().ToLowerInvariant() ?? "";
            var dateText = row[3]?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(company) &&
                string.IsNullOrWhiteSpace(title))
                continue;

            if (TryParseSheetDate(dateText, out var date))
                keys.Add((company, title, date));
        }

        return keys;
    }

    /// <inheritdoc/>
    public async Task AppendRowsAsync(
        IEnumerable<JobPosting> postings,
        CancellationToken ct = default)
    {
        var rows = postings
            .Select(p => (IList<object>)new List<object>
            {
                p.Company,
                p.Title,
                p.Url,
                p.PostingDate.ToString("yyyy-MM-dd"),
                p.Source,
                false,   // Applied?
                "",      // Application Date (auto-filled by Apps Script)
                false,   // Received Response?
                "",      // Response Date
                false,   // Received Interview?
                "",      // Interview Date
                p.Contact,
            })
            .ToList();

        if (rows.Count == 0) return;

        // The Apps Script pre-fills 200 rows with FALSE checkbox values and
        // background colours. The built-in Append API counts those as occupied
        // rows and would write after row 200. Instead, we explicitly scan
        // column A for the last row with a real company name and write there.
        var nextRow = await FindNextEmptyDataRowAsync(ct);

        var endRow     = nextRow + rows.Count - 1;
        var writeRange = $"Applications!A{nextRow}:L{endRow}";

        var body    = new ValueRange { Values = rows };
        var request = _service.Spreadsheets.Values.Update(body, _spreadsheetId, writeRange);

        // USER_ENTERED lets Sheets parse "false" as a boolean and the ISO date
        // string as a date, matching what a user would type in manually.
        request.ValueInputOption =
            SpreadsheetsResource.ValuesResource.UpdateRequest
                .ValueInputOptionEnum.USERENTERED;

        await request.ExecuteAsync(ct);
    }

    /// <summary>
    /// Returns the 1-based row number of the first empty data row in the
    /// Applications sheet. Scans columns A (Company), B (Title), and C (URL)
    /// — a row is considered occupied if any of those three cells is non-empty.
    /// This handles postings where the company name is blank (e.g. some
    /// government listings) without skipping over them.
    /// Returns 2 (first data row) if no data exists yet.
    /// </summary>
    private async Task<int> FindNextEmptyDataRowAsync(CancellationToken ct)
    {
        // Read columns A–C (Company, Title, URL) in one request.
        var request  = _service.Spreadsheets.Values.Get(_spreadsheetId, "Applications!A:C");
        var response = await request.ExecuteAsync(ct);

        if (response.Values is null || response.Values.Count <= 1)
            return 2; // No data rows yet — start at row 2 (row 1 is the header)

        // Walk backwards to find the last row that has any content in A, B, or C.
        // response.Values is 0-based; row index i → sheet row i+1.
        for (var i = response.Values.Count - 1; i >= 1; i--)
        {
            var row = response.Values[i];
            var hasData = row.Count > 0 &&
                          (IsNonEmpty(row, 0) ||   // A: Company
                           IsNonEmpty(row, 1) ||   // B: Title
                           IsNonEmpty(row, 2));     // C: URL

            if (hasData)
                return i + 2; // next row after the last occupied one
        }

        return 2;
    }

    private static bool IsNonEmpty(IList<object> row, int index) =>
        row.Count > index && !string.IsNullOrWhiteSpace(row[index]?.ToString());

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool TryParseSheetDate(string value, out DateOnly date)
    {
        foreach (var fmt in DateFormats)
        {
            if (DateOnly.TryParseExact(
                    value, fmt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date))
                return true;
        }

        date = default;
        return false;
    }
}

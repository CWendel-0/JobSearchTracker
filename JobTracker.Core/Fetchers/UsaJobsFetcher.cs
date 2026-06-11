using System.Text.Json;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches remote software engineering job postings from the USAJOBS API.
///
/// ── API registration ─────────────────────────────────────────────────────────
/// A free API key is required. Register at: https://developer.usajobs.gov/apirequest/
/// You will receive a key immediately. The email you register with becomes your
/// User-Agent value and must be stored in AppSettings.UsaJobsEmail.
///
/// ── Search strategy ──────────────────────────────────────────────────────────
/// The fetcher queries OPM occupational series 2210 (Information Technology
/// Management), which covers all federal software engineering, systems analysis,
/// and IT architecture roles. Combined with RemoteIndicator=True this returns
/// every remote IT posting across the federal government. The role and experience
/// filters in the pipeline clean the results further.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class UsaJobsFetcher : IJobFetcher
{
    public string SourceName => "USAJOBS";

    private const string BaseUrl      = "https://data.usajobs.gov/api/search";
    private const string CategoryCode = "2210";
    private const int    PageSize     = 25;
    private const int    MaxPages     = 20;   // cap at 500 results per run
    private const int    DaysBack     = 30;   // only postings from the last 30 days

    private readonly HttpClient _http;
    private readonly string     _apiKey;
    private readonly string     _email;      // registration email — used as User-Agent

    public UsaJobsFetcher(HttpClient http, string apiKey, string email)
    {
        _http   = http;
        _apiKey = apiKey;
        _email  = email;
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        var postings   = new List<JobPosting>();
        var page       = 1;
        var totalPages = 1;

        do
        {
            var url      = BuildUrl(page);
            var document = await GetPageAsync(url, ct);
            if (document is null) break;

            var (items, total) = ParsePage(document);
            postings.AddRange(items);

            totalPages = (int)Math.Ceiling(total / (double)PageSize);
            page++;

            // Brief pause between pages — the USAJOBS API asks for respectful use.
            if (page <= Math.Min(totalPages, MaxPages))
                await Task.Delay(300, ct);
        }
        while (page <= Math.Min(totalPages, MaxPages));

        return postings;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string BuildUrl(int page)
    {
        var parameters = new Dictionary<string, string>
        {
            ["JobCategoryCode"] = CategoryCode,
            ["RemoteIndicator"] = "True",
            ["ResultsPerPage"]  = PageSize.ToString(),
            ["Page"]            = page.ToString(),
            ["DatePosted"]      = DaysBack.ToString(),
        };

        var qs = string.Join("&",
            parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        return $"{BaseUrl}?{qs}";
    }

    private async Task<JsonDocument?> GetPageAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // USAJOBS requires these three headers on every request.
        request.Headers.TryAddWithoutValidation("User-Agent",        _email);
        request.Headers.TryAddWithoutValidation("Authorization-Key", _apiKey);
        request.Headers.TryAddWithoutValidation("Host", "data.usajobs.gov");

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    /// <summary>Parses a single page of results. Returns (postings, totalCount).</summary>
    public static (List<JobPosting> Postings, int TotalCount) ParsePage(JsonDocument doc)
    {
        var postings     = new List<JobPosting>();
        var searchResult = doc.RootElement.GetProperty("SearchResult");

        // TotalCount may be returned as a number or a quoted string.
        var countElem = searchResult.GetProperty("SearchResultCountAll");
        var total = countElem.ValueKind == JsonValueKind.Number
            ? countElem.GetInt32()
            : int.TryParse(countElem.GetString(), out var n) ? n : 0;

        if (!searchResult.TryGetProperty("SearchResultItems", out var items))
            return (postings, total);

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("MatchedObjectDescriptor", out var d))
                continue;

            var title   = GetStr(d, "PositionTitle");
            var company = GetStr(d, "OrganizationName");
            var url     = GetStr(d, "PositionURI");
            var dateStr = GetStr(d, "PublicationStartDate");
            var summary = GetStr(d, "QualificationSummary");

            var jobSummary = "";
            var contact    = "";
            if (d.TryGetProperty("UserArea",  out var ua) &&
                ua.TryGetProperty("Details",  out var det))
            {
                jobSummary = GetStr(det, "JobSummary");
                contact    = GetStr(det, "AgencyContactEmail");
            }

            // PublicationStartDate is ISO 8601 — take the date portion only.
            var datePart = dateStr.Contains('T') ? dateStr.Split('T')[0] : dateStr;
            if (!DateOnly.TryParse(datePart, out var postingDate))
                postingDate = DateOnly.FromDateTime(DateTime.UtcNow);

            postings.Add(new JobPosting
            {
                Company     = company,
                Title       = title,
                Url         = url,
                PostingDate = postingDate,
                Source      = "USAJOBS",
                Description = string.Join("\n", new[] { jobSummary, summary }
                                  .Where(s => !string.IsNullOrWhiteSpace(s))),
                Contact     = contact,
            });
        }

        return (postings, total);
    }

    private static string GetStr(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? "";
        return "";
    }
}

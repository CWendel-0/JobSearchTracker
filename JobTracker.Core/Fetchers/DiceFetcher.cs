using System.Text.Json;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches remote software engineering job postings from Dice.com.
///
/// ── Endpoint verification ─────────────────────────────────────────────────────
/// Dice exposes a public JSON search API that their own frontend calls. The URL
/// and response fields below were identified by inspecting network requests in
/// browser DevTools. If the endpoint changes, re-verify as follows:
///
///   1. Open https://www.dice.com/jobs?q=software+engineer&filters.workplaceTypes=Remote
///   2. Open DevTools → Network → filter by Fetch/XHR
///   3. Look for a request to job-search-api.dice.com containing JSON with a
///      "data" array of job objects
///   4. Update BaseUrl and the field names in ParsePage() if necessary.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class DiceFetcher : IJobFetcher
{
    public string SourceName => "Dice";

    private const string BaseUrl  = "https://job-search-api.dice.com/v1/jobs/search";
    private const int    PageSize = 20;
    private const int    MaxPages = 25;  // cap at 500 results per run

    // Search terms. The pipeline's RoleFilter will discard non-qualifying titles,
    // so it's better to cast a wider net here than to over-narrow the query.
    private static readonly string[] SearchTerms =
    [
        "software engineer",
        "devops engineer",
        "platform engineer",
        "data engineer",
        "site reliability engineer",
        "cloud engineer",
    ];

    private readonly HttpClient _http;

    public DiceFetcher(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        // Deduplicate across multiple search terms by tracking seen URLs.
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var postings = new List<JobPosting>();

        foreach (var term in SearchTerms)
        {
            var termPostings = await FetchTermAsync(term, seen, ct);
            postings.AddRange(termPostings);

            // Polite pause between different search terms.
            if (term != SearchTerms[^1])
                await Task.Delay(500, ct);
        }

        return postings;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<List<JobPosting>> FetchTermAsync(
        string          searchTerm,
        HashSet<string> seen,
        CancellationToken ct)
    {
        var postings   = new List<JobPosting>();
        var page       = 1;
        var totalPages = 1;

        do
        {
            var url      = BuildUrl(searchTerm, page);
            var document = await GetPageAsync(url, ct);
            if (document is null) break;

            var (items, total) = ParsePage(document);

            foreach (var posting in items)
            {
                if (seen.Add(posting.Url))
                    postings.Add(posting);
            }

            totalPages = (int)Math.Ceiling(total / (double)PageSize);
            page++;

            if (page <= Math.Min(totalPages, MaxPages))
                await Task.Delay(300, ct);
        }
        while (page <= Math.Min(totalPages, MaxPages));

        return postings;
    }

    private static string BuildUrl(string searchTerm, int page)
    {
        var parameters = new Dictionary<string, string>
        {
            ["q"]                        = searchTerm,
            ["countryCode"]              = "US",
            ["page"]                     = page.ToString(),
            ["pageSize"]                 = PageSize.ToString(),
            ["filters.workplaceTypes"]   = "Remote",
            ["filters.postedDate"]       = "MONTH",   // last 30 days
            ["language"]                 = "en",
        };

        var qs = string.Join("&",
            parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        return $"{BaseUrl}?{qs}";
    }

    private async Task<JsonDocument?> GetPageAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Dice's API expects a browser-like User-Agent. Without it some requests
        // may be blocked. Keep this header current if you encounter 403 errors.
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Origin", "https://www.dice.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.dice.com/");

        using var response = await _http.SendAsync(request, ct);

        // A 404 on the last page of results is normal for some endpoints.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    /// <summary>Parses a single page of Dice results.</summary>
    public static (List<JobPosting> Postings, int TotalCount) ParsePage(JsonDocument doc)
    {
        var postings = new List<JobPosting>();

        // ── Total count ──────────────────────────────────────────────────────
        // Located at root["meta"]["totalElements"] — verify field name in DevTools
        // if results appear truncated.
        var total = 0;
        if (doc.RootElement.TryGetProperty("meta",          out var meta) &&
            meta.TryGetProperty("totalElements", out var te))
        {
            total = te.ValueKind == JsonValueKind.Number
                ? te.GetInt32()
                : int.TryParse(te.GetString(), out var n) ? n : 0;
        }

        // ── Job items ────────────────────────────────────────────────────────
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return (postings, total);

        foreach (var item in data.EnumerateArray())
        {
            var title       = GetStr(item, "title");
            var company     = GetStr(item, "companyName");
            var applyUrl    = GetStr(item, "applyUrl");
            var description = GetStr(item, "descriptionFragment");
            var dateStr     = GetStr(item, "postedDate");

            if (string.IsNullOrWhiteSpace(applyUrl)) continue;

            // postedDate is ISO 8601 — take the date portion only.
            var datePart = dateStr.Contains('T') ? dateStr.Split('T')[0] : dateStr;
            if (!DateOnly.TryParse(datePart, out var postingDate))
                postingDate = DateOnly.FromDateTime(DateTime.UtcNow);

            postings.Add(new JobPosting
            {
                Company     = company,
                Title       = title,
                Url         = applyUrl,
                PostingDate = postingDate,
                Source      = "Dice",
                Description = description,
                Contact     = "",        // Dice does not expose recruiter contact in search results
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

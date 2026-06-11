using System.Text.Json;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches jobs from The Muse public API.
/// Requires a free API key from: https://www.themuse.com/developers/api/v2
///
/// The Muse skews toward mid-to-large tech companies with good culture data.
/// Results are filtered for remote locations in ParsePage().
/// </summary>
public class TheMuseFetcher : IJobFetcher
{
    public string SourceName => "The Muse";

    private const string BaseUrl  = "https://www.themuse.com/api/public/jobs";
    private const int    MaxPages = 20;

    // The Muse level names for senior roles.
    private static readonly string[] SeniorLevels =
        ["Senior Level", "Management", "Director"];

    private readonly HttpClient _http;
    private readonly string     _apiKey;

    public TheMuseFetcher(HttpClient http, string apiKey)
    {
        _http   = http;
        _apiKey = apiKey;
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        var postings = new List<JobPosting>();
        var page     = 1;
        var maxPage  = 1;

        do
        {
            var url = $"{BaseUrl}?api_key={Uri.EscapeDataString(_apiKey)}" +
                      $"&category=Software+Engineer" +
                      $"&page={page}";

            var document = await GetAsync(url, ct);
            if (document is null) break;

            var (jobs, pageCount) = ParsePage(document);
            postings.AddRange(jobs);

            maxPage = pageCount;
            page++;

            if (page <= Math.Min(maxPage, MaxPages))
                await Task.Delay(350, ct);
        }
        while (page <= Math.Min(maxPage, MaxPages));

        return postings;
    }

    internal static (List<JobPosting> Jobs, int PageCount) ParsePage(JsonDocument doc)
    {
        var jobs      = new List<JobPosting>();
        var pageCount = 1;
        var root      = doc.RootElement;

        if (root.TryGetProperty("page_count", out var pc))
            pageCount = pc.ValueKind == JsonValueKind.Number ? pc.GetInt32() : 1;

        if (!root.TryGetProperty("results", out var results))
            return (jobs, pageCount);

        foreach (var item in results.EnumerateArray())
        {
            var title   = GetStr(item, "name");
            var dateStr = GetStr(item, "publication_date");
            var url     = "";

            if (item.TryGetProperty("refs", out var refs))
                url = GetStr(refs, "landing_page");

            var company = "";
            if (item.TryGetProperty("company", out var co))
                company = GetStr(co, "name");

            // Keep only remote or flexible postings.
            var isRemote = false;
            if (item.TryGetProperty("locations", out var locs))
            {
                foreach (var loc in locs.EnumerateArray())
                {
                    var locName = GetStr(loc, "name").ToLowerInvariant();
                    if (locName.Contains("remote") || locName.Contains("flexible"))
                    {
                        isRemote = true;
                        break;
                    }
                }
            }
            if (!isRemote) continue;
            if (string.IsNullOrWhiteSpace(url)) continue;

            var datePart = dateStr.Contains('T') ? dateStr.Split('T')[0] : dateStr;
            if (!DateOnly.TryParse(datePart, out var date))
                date = DateOnly.FromDateTime(DateTime.UtcNow);

            jobs.Add(new JobPosting
            {
                Company     = company,
                Title       = title,
                Url         = url,
                PostingDate = date,
                Source      = "The Muse",
                Description = "",   // Detail page required; pipeline flag will handle
            });
        }

        return (jobs, pageCount);
    }

    private async Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static string GetStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";
}

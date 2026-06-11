using System.Text.Json;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches jobs from the Adzuna developer API.
/// Requires a free App ID and API Key from: https://developer.adzuna.com
///
/// Adzuna is a large aggregator with high volume. The role and experience
/// filters in the pipeline handle quality control after fetching.
/// </summary>
public class AdzunaFetcher : IJobFetcher
{
    public string SourceName => "Adzuna";

    private const string BaseUrl  = "https://api.adzuna.com/v1/api/jobs/us/search";
    private const int    PageSize = 50;
    private const int    MaxPages = 10;

    private static readonly string[] SearchTerms =
        ["software engineer", "devops engineer", "platform engineer", "data engineer"];

    private readonly HttpClient _http;
    private readonly string     _appId;
    private readonly string     _apiKey;

    public AdzunaFetcher(HttpClient http, string appId, string apiKey)
    {
        _http   = http;
        _appId  = appId;
        _apiKey = apiKey;
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var postings = new List<JobPosting>();

        foreach (var term in SearchTerms)
        {
            var page = 1;

            while (page <= MaxPages)
            {
                var url = $"{BaseUrl}/{page}" +
                          $"?app_id={Uri.EscapeDataString(_appId)}" +
                          $"&app_key={Uri.EscapeDataString(_apiKey)}" +
                          $"&what={Uri.EscapeDataString(term)}" +
                          $"&results_per_page={PageSize}" +
                          $"&full_time=1" +
                          $"&where=Remote" +
                          $"&content-type=application%2Fjson";

                var document = await GetAsync(url, ct);
                if (document is null) break;

                var (jobs, total) = ParsePage(document);
                foreach (var p in jobs)
                    if (seen.Add(p.Url)) postings.Add(p);

                if (page * PageSize >= total) break;
                page++;
                await Task.Delay(400, ct);
            }

            if (term != SearchTerms[^1])
                await Task.Delay(600, ct);
        }

        return postings;
    }

    internal static (List<JobPosting> Jobs, int Total) ParsePage(JsonDocument doc)
    {
        var jobs  = new List<JobPosting>();
        var total = 0;
        var root  = doc.RootElement;

        if (root.TryGetProperty("count", out var countProp))
            total = countProp.ValueKind == JsonValueKind.Number ? countProp.GetInt32() : 0;

        if (!root.TryGetProperty("results", out var results))
            return (jobs, total);

        foreach (var item in results.EnumerateArray())
        {
            var title   = GetStr(item, "title");
            var url     = GetStr(item, "redirect_url");
            var desc    = GetStr(item, "description");
            var dateStr = GetStr(item, "created");

            var company = "";
            if (item.TryGetProperty("company", out var co))
                company = GetStr(co, "display_name");

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
                Source      = "Adzuna",
                Description = desc,
            });
        }

        return (jobs, total);
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

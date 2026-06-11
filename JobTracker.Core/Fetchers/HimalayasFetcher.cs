using System.Text.Json;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches remote tech jobs from the Himalayas public API.
/// No authentication required.
///
/// ── Endpoint verification ────────────────────────────────────────────────────
/// The Himalayas API endpoint is at https://himalayas.app/jobs/api
/// If results come back empty, verify by opening https://himalayas.app/jobs in a
/// browser, inspecting Network → XHR, and confirming the current endpoint URL
/// and response field names. Update BaseUrl and ParseJobs() accordingly.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class HimalayasFetcher : IJobFetcher
{
    public string SourceName => "Himalayas";

    private const string BaseUrl  = "https://himalayas.app/jobs/api";
    private const int    PageSize = 100;
    private const int    MaxPages = 10;

    private static readonly string[] SearchTerms =
        ["software engineer", "devops", "platform engineer", "data engineer"];

    private readonly HttpClient _http;

    public HimalayasFetcher(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var postings = new List<JobPosting>();

        foreach (var term in SearchTerms)
        {
            var offset = 0;
            var page   = 0;

            while (page < MaxPages)
            {
                var url      = $"{BaseUrl}?q={Uri.EscapeDataString(term)}" +
                               $"&limit={PageSize}&offset={offset}";
                var document = await GetAsync(url, ct);
                if (document is null) break;

                var (jobs, total) = ParseJobs(document);
                foreach (var p in jobs)
                    if (seen.Add(p.Url)) postings.Add(p);

                offset += PageSize;
                page++;

                if (offset >= total) break;
                await Task.Delay(300, ct);
            }

            if (term != SearchTerms[^1])
                await Task.Delay(500, ct);
        }

        return postings;
    }

    internal static (List<JobPosting> Jobs, int Total) ParseJobs(JsonDocument doc)
    {
        var jobs  = new List<JobPosting>();
        var total = 0;

        var root = doc.RootElement;

        if (root.TryGetProperty("total", out var totalProp))
            total = totalProp.ValueKind == JsonValueKind.Number ? totalProp.GetInt32() : 0;

        // The array may be at root["jobs"] or root["data"] — check both.
        JsonElement items;
        if (!root.TryGetProperty("jobs", out items) &&
            !root.TryGetProperty("data", out items))
            return (jobs, total);

        foreach (var item in items.EnumerateArray())
        {
            var title    = GetStr(item, "title");
            var company  = GetStr(item, "companyName");
            var url      = GetStr(item, "applyUrl");
            var desc     = GetStr(item, "description");
            var dateStr  = GetStr(item, "publishedAt");

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
                Source      = "Himalayas",
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

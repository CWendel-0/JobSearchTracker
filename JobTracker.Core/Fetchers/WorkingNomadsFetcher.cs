using System.Text.Json;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches remote tech jobs from the Working Nomads public API.
/// No authentication required.
///
/// ── Endpoint verification ────────────────────────────────────────────────────
/// Working Nomads exposes a public API at:
///   https://www.workingnomads.com/api/exposed_jobs/?category=development
///
/// If results come back empty, verify by inspecting https://www.workingnomads.com
/// in browser DevTools (Network → XHR) and updating BaseUrl or the category
/// parameter names in BuildUrl() to match the current API.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class WorkingNomadsFetcher : IJobFetcher
{
    public string SourceName => "Working Nomads";

    private const string BaseUrl = "https://www.workingnomads.com/api/exposed_jobs/";

    private static readonly string[] Categories = ["development", "devops-sysadmin"];

    private readonly HttpClient _http;

    public WorkingNomadsFetcher(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var postings = new List<JobPosting>();

        foreach (var category in Categories)
        {
            var url      = $"{BaseUrl}?category={Uri.EscapeDataString(category)}";
            var document = await GetAsync(url, ct);
            if (document is null) continue;

            foreach (var p in ParseJobs(document))
                if (seen.Add(p.Url)) postings.Add(p);

            if (category != Categories[^1])
                await Task.Delay(400, ct);
        }

        return postings;
    }

    internal static IEnumerable<JobPosting> ParseJobs(JsonDocument doc)
    {
        // Response is either a root array or {"results": [...]}
        JsonElement items;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            items = doc.RootElement;
        else if (!doc.RootElement.TryGetProperty("results", out items))
            yield break;

        foreach (var item in items.EnumerateArray())
        {
            var title   = GetStr(item, "title");
            var company = GetStr(item, "company");
            var url     = GetStr(item, "url");
            var desc    = GetStr(item, "description");
            var dateStr = GetStr(item, "pub_date");

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
                continue;

            var datePart = dateStr.Contains('T') ? dateStr.Split('T')[0] : dateStr;
            if (!DateOnly.TryParse(datePart, out var date))
                date = DateOnly.FromDateTime(DateTime.UtcNow);

            yield return new JobPosting
            {
                Company     = company,
                Title       = title,
                Url         = url,
                PostingDate = date,
                Source      = "Working Nomads",
                Description = desc,
            };
        }
    }

    private async Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
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

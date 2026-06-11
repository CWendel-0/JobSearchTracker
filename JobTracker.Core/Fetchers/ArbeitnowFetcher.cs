using System.Text.Json;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches remote jobs from the Arbeitnow public job board API.
/// No authentication required.
/// Docs: https://www.arbeitnow.com/api
///
/// The API returns all remote-tagged jobs. The role filter in the pipeline
/// discards non-engineering titles, so no keyword filtering is applied here.
/// </summary>
public class ArbeitnowFetcher : IJobFetcher
{
    public string SourceName => "Arbeitnow";

    private const string BaseUrl  = "https://www.arbeitnow.com/api/job-board-api";
    private const int    MaxPages = 15;

    private readonly HttpClient _http;

    public ArbeitnowFetcher(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        var postings  = new List<JobPosting>();
        var page      = 1;
        var lastPage  = 1;

        do
        {
            var url      = $"{BaseUrl}?page={page}";
            var document = await GetAsync(url, ct);
            if (document is null) break;

            var (jobs, last) = ParsePage(document);
            postings.AddRange(jobs);
            lastPage = last;
            page++;

            if (page <= Math.Min(lastPage, MaxPages))
                await Task.Delay(300, ct);
        }
        while (page <= Math.Min(lastPage, MaxPages));

        return postings;
    }

    internal static (List<JobPosting> Jobs, int LastPage) ParsePage(JsonDocument doc)
    {
        var jobs     = new List<JobPosting>();
        var lastPage = 1;

        var root = doc.RootElement;

        if (root.TryGetProperty("meta", out var meta) &&
            meta.TryGetProperty("last_page", out var lp))
            lastPage = lp.ValueKind == JsonValueKind.Number ? lp.GetInt32() : 1;

        if (!root.TryGetProperty("data", out var data))
            return (jobs, lastPage);

        foreach (var item in data.EnumerateArray())
        {
            // Skip non-remote postings.
            if (item.TryGetProperty("remote", out var remoteProp) &&
                remoteProp.ValueKind == JsonValueKind.False)
                continue;

            var title   = GetStr(item, "title");
            var company = GetStr(item, "company_name");
            var url     = GetStr(item, "url");
            var desc    = GetStr(item, "description");

            if (string.IsNullOrWhiteSpace(url)) continue;

            // created_at may be a Unix timestamp (number) or ISO string.
            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            if (item.TryGetProperty("created_at", out var createdAt))
            {
                if (createdAt.ValueKind == JsonValueKind.Number)
                    date = DateOnly.FromDateTime(
                        DateTimeOffset.FromUnixTimeSeconds(createdAt.GetInt64()).UtcDateTime);
                else if (createdAt.ValueKind == JsonValueKind.String &&
                         DateOnly.TryParse(createdAt.GetString()?.Split('T')[0], out var parsed))
                    date = parsed;
            }

            jobs.Add(new JobPosting
            {
                Company     = company,
                Title       = title,
                Url         = url,
                PostingDate = date,
                Source      = "Arbeitnow",
                Description = desc,
            });
        }

        return (jobs, lastPage);
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

using System.Text.Json;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches remote software engineering jobs from the Remotive public API.
/// No authentication required.
/// Docs: https://remotive.com/api/remote-jobs
/// </summary>
public class RemotiveFetcher : IJobFetcher
{
    public string SourceName => "Remotive";

    private const string BaseUrl = "https://remotive.com/api/remote-jobs";

    // Remotive uses category slugs to narrow results before the role filter runs.
    private static readonly string[] Categories =
        ["software-dev", "devops-sysadmin", "data"];

    private readonly HttpClient _http;

    public RemotiveFetcher(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var postings = new List<JobPosting>();

        foreach (var category in Categories)
        {
            var url      = $"{BaseUrl}?category={Uri.EscapeDataString(category)}&limit=100";
            var document = await GetAsync(url, ct);
            if (document is null) continue;

            foreach (var p in ParseJobs(document))
            {
                if (seen.Add(p.Url))
                    postings.Add(p);
            }

            if (category != Categories[^1])
                await Task.Delay(400, ct);
        }

        return postings;
    }

    internal static IEnumerable<JobPosting> ParseJobs(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("jobs", out var jobs))
            yield break;

        foreach (var item in jobs.EnumerateArray())
        {
            var title       = GetStr(item, "title");
            var company     = GetStr(item, "company_name");
            var url         = GetStr(item, "url");
            var description = GetStr(item, "description");
            var dateStr     = GetStr(item, "publication_date");

            if (string.IsNullOrWhiteSpace(url)) continue;

            var datePart = dateStr.Contains('T') ? dateStr.Split('T')[0] : dateStr;
            if (!DateOnly.TryParse(datePart, out var date))
                date = DateOnly.FromDateTime(DateTime.UtcNow);

            yield return new JobPosting
            {
                Company     = company,
                Title       = title,
                Url         = url,
                PostingDate = date,
                Source      = "Remotive",
                Description = description,
            };
        }
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

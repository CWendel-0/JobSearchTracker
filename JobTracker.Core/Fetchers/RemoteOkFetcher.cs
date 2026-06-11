using System.Text.Json;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches remote tech jobs from the Remote OK public JSON feed.
/// No authentication required.
/// Feed: https://remoteok.com/remote-jobs.json
///
/// Remote OK's feed is a flat array. The first element is a legal/notice object
/// (no "title" field) — it is skipped automatically by the parser.
/// </summary>
public class RemoteOkFetcher : IJobFetcher
{
    public string SourceName => "Remote OK";

    private const string FeedUrl = "https://remoteok.com/remote-jobs.json";

    private readonly HttpClient _http;

    public RemoteOkFetcher(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);
        // Remote OK requires a non-empty User-Agent.
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return [];

        var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        return ParseJobs(doc).ToList();
    }

    internal static IEnumerable<JobPosting> ParseJobs(JsonDocument doc)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            // Skip the first notice element (has no "title").
            if (!item.TryGetProperty("title", out var titleProp) ||
                titleProp.ValueKind != JsonValueKind.String)
                continue;

            var title       = titleProp.GetString() ?? "";
            var company     = GetStr(item, "company");
            var url         = GetStr(item, "url");
            var description = GetStr(item, "description");
            var dateStr     = GetStr(item, "date");

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
                Source      = "Remote OK",
                Description = description,
            };
        }
    }

    private static string GetStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";
}

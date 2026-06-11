using System.Xml.Linq;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches remote programming and DevOps jobs from We Work Remotely RSS feeds.
/// No authentication required.
///
/// We Work Remotely publishes per-category RSS 2.0 feeds. Titles in the feed
/// follow the format "Company Name | Job Title" and are split on " | ".
/// </summary>
public class WeWorkRemotelyFetcher : IJobFetcher
{
    public string SourceName => "We Work Remotely";

    // Category-specific RSS feeds.
    private static readonly string[] FeedUrls =
    [
        "https://weworkremotely.com/categories/remote-programming-jobs.rss",
        "https://weworkremotely.com/categories/remote-devops-sysadmin-jobs.rss",
        "https://weworkremotely.com/categories/remote-data-science-jobs.rss",
    ];

    private readonly HttpClient _http;

    public WeWorkRemotelyFetcher(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var postings = new List<JobPosting>();

        foreach (var feedUrl in FeedUrls)
        {
            var jobs = await FetchFeedAsync(feedUrl, ct);
            foreach (var p in jobs)
                if (seen.Add(p.Url)) postings.Add(p);

            if (feedUrl != FeedUrls[^1])
                await Task.Delay(500, ct);
        }

        return postings;
    }

    private async Task<List<JobPosting>> FetchFeedAsync(string feedUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return [];

        var xml = await response.Content.ReadAsStringAsync(ct);
        return ParseFeed(xml);
    }

    internal static List<JobPosting> ParseFeed(string xml)
    {
        var postings = new List<JobPosting>();

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return postings; }

        var items = doc.Descendants("item");

        foreach (var item in items)
        {
            var rawTitle  = item.Element("title")?.Value ?? "";
            var link      = GetLink(item);
            var pubDate   = item.Element("pubDate")?.Value ?? "";
            var desc      = item.Element("description")?.Value ?? "";

            if (string.IsNullOrWhiteSpace(link)) continue;

            // Titles follow "Company Name | Job Title" — split on the pipe.
            var parts   = rawTitle.Split('|', 2);
            var company = parts.Length == 2 ? parts[0].Trim() : "";
            var title   = parts.Length == 2 ? parts[1].Trim() : rawTitle.Trim();

            // WWR sometimes prefixes titles with the location region in brackets.
            // e.g. "[Worldwide] Senior Software Engineer" — strip the bracket prefix.
            if (title.StartsWith('['))
            {
                var bracketEnd = title.IndexOf(']');
                if (bracketEnd >= 0)
                    title = title[(bracketEnd + 1)..].Trim();
            }

            // pubDate is RFC 2822: "Mon, 15 May 2026 00:00:00 +0000"
            if (!DateTimeOffset.TryParse(pubDate, out var dto))
                dto = DateTimeOffset.UtcNow;

            postings.Add(new JobPosting
            {
                Company     = company,
                Title       = title,
                Url         = link,
                PostingDate = DateOnly.FromDateTime(dto.UtcDateTime),
                Source      = "We Work Remotely",
                Description = StripHtml(desc),
            });
        }

        return postings;
    }

    /// <summary>
    /// In WWR RSS, the link is placed between the &lt;guid&gt; and next element
    /// as a text node. The helper checks both the &lt;link&gt; element and the
    /// &lt;guid&gt; element which also carries the canonical URL.
    /// </summary>
    private static string GetLink(XElement item)
    {
        var link = item.Element("link")?.Value;
        if (!string.IsNullOrWhiteSpace(link)) return link;

        // Fall back to the guid (it is a permalink for WWR).
        var guid = item.Element("guid")?.Value;
        return guid ?? "";
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        // Minimal strip — remove tags, decode common entities.
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
            .Replace("&amp;",  "&")
            .Replace("&lt;",   "<")
            .Replace("&gt;",   ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;",  "'")
            .Replace("&nbsp;", " ")
            .Trim();
    }
}

using HtmlAgilityPack;
using JobTracker.Core.Models;

namespace JobTracker.Core.Fetchers;

/// <summary>
/// Fetches remote software engineering job postings from RemoteJobsFinder.com
/// by scraping HTML with HtmlAgilityPack.
///
/// ── Selector verification ─────────────────────────────────────────────────────
/// HTML scrapers need updating when a site redesigns. If this fetcher stops
/// returning results, verify the selectors as follows:
///
///   1. Open https://remotejobsfinder.com in a browser
///   2. Navigate to a software engineering job listing
///   3. Open DevTools → Inspector and identify the elements that contain:
///        - The list of job cards (update JobListSelector)
///        - Within each card: title, company, date, detail URL
///   4. Open a job detail page and identify the description container
///        (update DescriptionSelector)
///   5. Update the constants below to match.
///
/// ── Pagination ────────────────────────────────────────────────────────────────
/// The fetcher looks for a "next page" link after each page. Update
/// NextPageSelector if the site uses a different pagination pattern.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class RemoteJobsFinderFetcher : IJobFetcher
{
    public string SourceName => "RemoteJobsFinder";

    private const string BaseUrl  = "https://remotejobsfinder.com";
    private const int    MaxPages = 10;

    // ── CSS / XPath selectors ─────────────────────────────────────────────────
    // Verify these against the live site and update as needed (see class summary).

    // XPath for the repeating job card container on a listing page.
    private const string JobCardXPath =
        "//div[contains(@class,'job-card')] | //article[contains(@class,'job')]";

    // XPath relative to each job card.
    private const string TitleXPath   = ".//h2 | .//h3 | .//*[contains(@class,'job-title')]";
    private const string CompanyXPath = ".//*[contains(@class,'company')] | .//*[contains(@class,'employer')]";
    private const string DateXPath    = ".//*[contains(@class,'date')] | .//time";
    private const string LinkXPath    = ".//a[contains(@href,'/job')] | .//a[contains(@href,'/jobs')]";

    // XPath for the description on a job detail page.
    private const string DescriptionXPath =
        "//div[contains(@class,'description')] | //div[contains(@class,'job-detail')]";

    // XPath for the "next page" link.
    private const string NextPageXPath =
        "//a[contains(@class,'next') or contains(@rel,'next') or contains(text(),'Next')]";

    // URL paths to search — one per role category the site supports.
    // Update to match the site's actual category URLs.
    private static readonly string[] SearchPaths =
    [
        "/jobs/software-engineer/",
        "/jobs/devops/",
        "/jobs/data-engineer/",
        "/jobs/platform-engineer/",
    ];

    private readonly HttpClient _http;

    public RemoteJobsFinderFetcher(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<JobPosting>> FetchAsync(CancellationToken ct = default)
    {
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var postings = new List<JobPosting>();

        foreach (var path in SearchPaths)
        {
            var pathPostings = await FetchPathAsync(path, seen, ct);
            postings.AddRange(pathPostings);

            if (path != SearchPaths[^1])
                await Task.Delay(600, ct);
        }

        return postings;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<List<JobPosting>> FetchPathAsync(
        string          path,
        HashSet<string> seen,
        CancellationToken ct)
    {
        var postings    = new List<JobPosting>();
        var currentUrl  = BaseUrl + path;
        var pagesLoaded = 0;

        while (!string.IsNullOrEmpty(currentUrl) && pagesLoaded < MaxPages)
        {
            var html = await GetHtmlAsync(currentUrl, ct);
            if (html is null) break;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var cards = doc.DocumentNode.SelectNodes(JobCardXPath);
            if (cards is null) break;

            foreach (var card in cards)
            {
                var posting = ParseCard(card);
                if (posting is null) continue;

                // Resolve the detail URL and fill in the description.
                var detailUrl = ResolveUrl(posting.Url);
                if (!seen.Add(detailUrl)) continue;

                // Re-create with the resolved URL.
                posting = posting with { Url = detailUrl };

                // Fetch full description from the detail page.
                var description = await FetchDescriptionAsync(detailUrl, ct);
                if (!string.IsNullOrWhiteSpace(description))
                    posting = posting with { Description = description };

                postings.Add(posting);
                await Task.Delay(400, ct);   // polite delay per detail page
            }

            // Follow the next page link, if present.
            var nextNode = doc.DocumentNode.SelectSingleNode(NextPageXPath);
            var nextHref = nextNode?.GetAttributeValue("href", "");
            currentUrl   = string.IsNullOrWhiteSpace(nextHref) ? "" : ResolveUrl(nextHref);
            pagesLoaded++;
        }

        return postings;
    }

    private JobPosting? ParseCard(HtmlNode card)
    {
        var titleNode   = card.SelectSingleNode(TitleXPath);
        var companyNode = card.SelectSingleNode(CompanyXPath);
        var dateNode    = card.SelectSingleNode(DateXPath);
        var linkNode    = card.SelectSingleNode(LinkXPath);

        var title   = HtmlEntity.DeEntitize(titleNode?.InnerText.Trim()   ?? "");
        var company = HtmlEntity.DeEntitize(companyNode?.InnerText.Trim() ?? "");
        var href    = linkNode?.GetAttributeValue("href", "") ?? "";

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
            return null;

        // Try the datetime attribute first (machine-readable), then inner text.
        var dateTimeAttr = dateNode?.GetAttributeValue("datetime", "") ?? "";
        var dateStr      = dateTimeAttr.Length > 0
            ? dateTimeAttr
            : dateNode?.InnerText.Trim() ?? "";

        if (!DateOnly.TryParse(dateStr, out var postingDate))
            postingDate = DateOnly.FromDateTime(DateTime.UtcNow);

        return new JobPosting
        {
            Company     = company,
            Title       = title,
            Url         = href,
            PostingDate = postingDate,
            Source      = "RemoteJobsFinder",
            Description = "",    // filled in after fetching the detail page
            Contact     = "",
        };
    }

    private async Task<string?> FetchDescriptionAsync(string detailUrl, CancellationToken ct)
    {
        var html = await GetHtmlAsync(detailUrl, ct);
        if (html is null) return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var node = doc.DocumentNode.SelectSingleNode(DescriptionXPath);
        return node is null ? null : HtmlEntity.DeEntitize(node.InnerText.Trim());
    }

    private async Task<string?> GetHtmlAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/124.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadAsStringAsync(ct);
    }

    private string ResolveUrl(string href)
    {
        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return href;

        return href.StartsWith('/')
            ? BaseUrl + href
            : BaseUrl + "/" + href;
    }
}

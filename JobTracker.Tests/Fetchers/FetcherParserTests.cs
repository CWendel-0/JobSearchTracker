using System.Text.Json;
using JobTracker.Core.Fetchers;
using JobTracker.Core.Models;

namespace JobTracker.Tests.Fetchers;

/// <summary>
/// Unit tests for fetcher parsing logic using fixture JSON.
/// No HTTP calls are made — the parsers are exercised in isolation.
///
/// Integration tests (live API calls) are not included here; run those manually
/// by calling FetchAsync() directly from a console app or test with a
/// [Trait("Category","Integration")] attribute and skip in CI.
/// </summary>
public class FetcherParserTests
{
    // ── UsaJobsFetcher ────────────────────────────────────────────────────────

    [Fact]
    public void UsaJobs_ParsePage_ReturnsCorrectPostingCount()
    {
        var doc    = JsonDocument.Parse(UsaJobsFixture);
        var (postings, total) = UsaJobsFetcher.ParsePage(doc);

        Assert.Equal(150, total);
        Assert.Equal(2,   postings.Count);
    }

    [Fact]
    public void UsaJobs_ParsePage_MapsFieldsCorrectly()
    {
        var doc     = JsonDocument.Parse(UsaJobsFixture);
        var (postings, _) = UsaJobsFetcher.ParsePage(doc);
        var posting = postings[0];

        Assert.Equal("Department of Defense",                posting.Company);
        Assert.Equal("Software Engineer",                    posting.Title);
        Assert.Equal("https://www.usajobs.gov/job/12345",    posting.Url);
        Assert.Equal(new DateOnly(2026, 5, 10),              posting.PostingDate);
        Assert.Equal("USAJOBS",                              posting.Source);
        Assert.Contains("10 years",                          posting.Description);
        Assert.Equal("recruiter@dod.gov",                    posting.Contact);
    }

    [Fact]
    public void UsaJobs_ParsePage_HandlesStringTotalCount()
    {
        // Some API versions return SearchResultCountAll as a quoted string.
        const string json = """
        {
          "SearchResult": {
            "SearchResultCountAll": "42",
            "SearchResultItems": []
          }
        }
        """;

        var (_, total) = UsaJobsFetcher.ParsePage(JsonDocument.Parse(json));
        Assert.Equal(42, total);
    }

    [Fact]
    public void UsaJobs_ParsePage_HandlesIso8601DateWithTimePart()
    {
        const string json = """
        {
          "SearchResult": {
            "SearchResultCountAll": 1,
            "SearchResultItems": [{
              "MatchedObjectDescriptor": {
                "PositionTitle": "Dev",
                "OrganizationName": "Agency",
                "PositionURI": "https://www.usajobs.gov/job/1",
                "PublicationStartDate": "2026-03-15T00:00:00Z",
                "QualificationSummary": ""
              }
            }]
          }
        }
        """;

        var (postings, _) = UsaJobsFetcher.ParsePage(JsonDocument.Parse(json));
        Assert.Equal(new DateOnly(2026, 3, 15), postings[0].PostingDate);
    }

    [Fact]
    public void UsaJobs_ParsePage_ReturnsEmptyForMissingItems()
    {
        const string json = """
        {
          "SearchResult": {
            "SearchResultCountAll": 0
          }
        }
        """;

        var (postings, total) = UsaJobsFetcher.ParsePage(JsonDocument.Parse(json));
        Assert.Empty(postings);
        Assert.Equal(0, total);
    }

    // ── DiceFetcher ───────────────────────────────────────────────────────────

    [Fact]
    public void Dice_ParsePage_ReturnsCorrectPostingCount()
    {
        var doc    = JsonDocument.Parse(DiceFixture);
        var (postings, total) = DiceFetcher.ParsePage(doc);

        Assert.Equal(300, total);
        Assert.Equal(2,   postings.Count);
    }

    [Fact]
    public void Dice_ParsePage_MapsFieldsCorrectly()
    {
        var doc     = JsonDocument.Parse(DiceFixture);
        var (postings, _) = DiceFetcher.ParsePage(doc);
        var posting = postings[0];

        Assert.Equal("Acme Corp",                                   posting.Company);
        Assert.Equal("Senior Software Engineer",                    posting.Title);
        Assert.Equal("https://www.dice.com/jobs/detail/abc123",     posting.Url);
        Assert.Equal(new DateOnly(2026, 5, 12),                     posting.PostingDate);
        Assert.Equal("Dice",                                        posting.Source);
        Assert.Contains("distributed systems",                      posting.Description);
    }

    [Fact]
    public void Dice_ParsePage_SkipsItemsWithEmptyUrl()
    {
        const string json = """
        {
          "data": [
            { "title": "Engineer", "companyName": "Corp", "applyUrl": "", "postedDate": "2026-05-01T00:00:00Z", "descriptionFragment": "..." },
            { "title": "Engineer", "companyName": "Corp", "applyUrl": "https://dice.com/jobs/1", "postedDate": "2026-05-01T00:00:00Z", "descriptionFragment": "..." }
          ],
          "meta": { "totalElements": 2 }
        }
        """;

        var (postings, _) = DiceFetcher.ParsePage(JsonDocument.Parse(json));
        Assert.Single(postings);
    }

    [Fact]
    public void Dice_ParsePage_ReturnsEmptyForMissingDataArray()
    {
        const string json = """{ "meta": { "totalElements": 0 } }""";
        var (postings, _) = DiceFetcher.ParsePage(JsonDocument.Parse(json));
        Assert.Empty(postings);
    }

    // ── Fixture JSON ──────────────────────────────────────────────────────────

    private const string UsaJobsFixture = """
    {
      "SearchResult": {
        "SearchResultCount":    2,
        "SearchResultCountAll": 150,
        "SearchResultItems": [
          {
            "MatchedObjectDescriptor": {
              "PositionTitle":        "Software Engineer",
              "OrganizationName":     "Department of Defense",
              "PositionURI":          "https://www.usajobs.gov/job/12345",
              "PublicationStartDate": "2026-05-10T00:00:00Z",
              "QualificationSummary": "Requires 10 years of software engineering experience.",
              "UserArea": {
                "Details": {
                  "JobSummary":        "Design and implement enterprise software systems.",
                  "AgencyContactEmail":"recruiter@dod.gov"
                }
              }
            }
          },
          {
            "MatchedObjectDescriptor": {
              "PositionTitle":        "IT Specialist (SYSANALYSIS)",
              "OrganizationName":     "Department of Homeland Security",
              "PositionURI":          "https://www.usajobs.gov/job/67890",
              "PublicationStartDate": "2026-05-08T00:00:00Z",
              "QualificationSummary": "Minimum 9 years of IT systems analysis experience.",
              "UserArea": {
                "Details": {
                  "JobSummary":        "Analyse and design complex systems.",
                  "AgencyContactEmail":""
                }
              }
            }
          }
        ]
      }
    }
    """;

    private const string DiceFixture = """
    {
      "data": [
        {
          "title":               "Senior Software Engineer",
          "companyName":         "Acme Corp",
          "applyUrl":            "https://www.dice.com/jobs/detail/abc123",
          "postedDate":          "2026-05-12T00:00:00.000Z",
          "descriptionFragment": "Seeking an engineer with experience in distributed systems."
        },
        {
          "title":               "Lead DevOps Engineer",
          "companyName":         "TechFirm LLC",
          "applyUrl":            "https://www.dice.com/jobs/detail/def456",
          "postedDate":          "2026-05-11T00:00:00.000Z",
          "descriptionFragment": "10+ years of infrastructure and DevOps experience required."
        }
      ],
      "meta": {
        "totalElements": 300,
        "pageSize":       20,
        "currentPage":     1
      }
    }
    """;
}

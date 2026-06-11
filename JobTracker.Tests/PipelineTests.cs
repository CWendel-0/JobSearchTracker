using JobTracker.Core;
using JobTracker.Core.Filters;
using JobTracker.Core.Models;
using Moq;

namespace JobTracker.Tests;

public class PipelineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Pipeline BuildPipeline(
        Mock<ISheetsClient> mockSheets,
        IEnumerable<string>? extraInclude = null,
        IEnumerable<string>? extraExclude = null)
        => new(
            mockSheets.Object,
            new RoleFilter(extraInclude, extraExclude),
            new ExperienceFilter());

    /// <summary>
    /// Returns a mock sheets client whose GetExistingKeysAsync returns the
    /// supplied set of keys (default: empty — no existing postings).
    /// </summary>
    private static Mock<ISheetsClient> MockSheets(
        HashSet<(string, string, DateOnly)>? existingKeys = null)
    {
        var mock = new Mock<ISheetsClient>();
        mock.Setup(s => s.GetExistingKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingKeys ?? []);
        return mock;
    }

    private static JobPosting MakePosting(
        string  company     = "Acme Corp",
        string  title       = "Senior Software Engineer",
        string  description = "Requires 10+ years of software engineering experience.",
        string  source      = "Dice",
        DateOnly? date      = null)
        => new()
        {
            Company     = company,
            Title       = title,
            Url         = "https://example.com/job/1",
            PostingDate = date ?? new DateOnly(2026, 5, 15),
            Source      = source,
            Description = description,
            Contact     = "",
        };

    // ── Role filtering ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_ExcludesNonQualifyingRole()
    {
        var mock     = MockSheets();
        var pipeline = BuildPipeline(mock);
        var postings = new[] { MakePosting(title: "Product Manager") };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Empty(result.NewPostings);
        Assert.Equal(1, result.RoleFiltered);
        Assert.Equal(0, result.ExperienceFiltered);
        Assert.Equal(0, result.Duplicates);
    }

    [Fact]
    public async Task ProcessAsync_IncludesQualifyingRole()
    {
        var mock     = MockSheets();
        var pipeline = BuildPipeline(mock);
        var postings = new[] { MakePosting(title: "Senior Software Engineer") };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Single(result.NewPostings);
        Assert.Equal(0, result.RoleFiltered);
    }

    // ── Experience filtering ──────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_ExcludesSubThresholdExperience()
    {
        var mock     = MockSheets();
        var pipeline = BuildPipeline(mock);
        var postings = new[]
        {
            MakePosting(description: "Requires 2+ years of software engineering experience.")
        };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Empty(result.NewPostings);
        Assert.Equal(1, result.ExperienceFiltered);
        Assert.Equal(0, result.RoleFiltered);
    }

    [Fact]
    public async Task ProcessAsync_IncludesPostingMeetingExperienceThreshold()
    {
        var mock     = MockSheets();
        var pipeline = BuildPipeline(mock);
        var postings = new[]
        {
            MakePosting(description: "Minimum 8 years of software engineering experience.")
        };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Single(result.NewPostings);
        Assert.Equal(ExperienceLevel.Senior, result.NewPostings[0].Level);
    }

    [Fact]
    public async Task ProcessAsync_FlagsPostingWithNoExperienceStated()
    {
        var mock     = MockSheets();
        var pipeline = BuildPipeline(mock);
        var postings = new[]
        {
            MakePosting(title: "Software Engineer", description: "Join our talented engineering team.")
        };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Single(result.NewPostings);
        Assert.Equal(ExperienceLevel.Unknown, result.NewPostings[0].Level);
        Assert.Equal(0, result.ExperienceFiltered);
    }

    // ── Deduplication against sheet ───────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_SkipsPostingAlreadyInSheet()
    {
        var existingDate = new DateOnly(2026, 5, 1);
        var existingKeys = new HashSet<(string, string, DateOnly)>
        {
            ("acme corp", "senior software engineer", existingDate)
        };

        var mock     = MockSheets(existingKeys);
        var pipeline = BuildPipeline(mock);
        var postings = new[]
        {
            MakePosting(company: "Acme Corp", title: "Senior Software Engineer",
                        date: existingDate)
        };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Empty(result.NewPostings);
        Assert.Equal(1, result.Duplicates);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateCheckIsCaseInsensitive()
    {
        var date = new DateOnly(2026, 5, 1);
        var existingKeys = new HashSet<(string, string, DateOnly)>
        {
            ("acme corp", "senior software engineer", date)
        };

        var mock     = MockSheets(existingKeys);
        var pipeline = BuildPipeline(mock);

        // Different casing — should still be detected as a duplicate
        var postings = new[]
        {
            MakePosting(company: "ACME CORP", title: "SENIOR SOFTWARE ENGINEER",
                        date: date)
        };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Empty(result.NewPostings);
        Assert.Equal(1, result.Duplicates);
    }

    [Fact]
    public async Task ProcessAsync_SameTitleDifferentDateIsNotDuplicate()
    {
        var existingKeys = new HashSet<(string, string, DateOnly)>
        {
            ("acme corp", "senior software engineer", new DateOnly(2026, 4, 1))
        };

        var mock     = MockSheets(existingKeys);
        var pipeline = BuildPipeline(mock);

        // Same company + title but different date — a re-post, treat as new
        var postings = new[]
        {
            MakePosting(company: "Acme Corp", title: "Senior Software Engineer",
                        date: new DateOnly(2026, 5, 15))
        };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Single(result.NewPostings);
        Assert.Equal(0, result.Duplicates);
    }

    // ── Within-batch deduplication ────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_DeduplicatesWithinTheSameBatch()
    {
        var mock     = MockSheets();
        var pipeline = BuildPipeline(mock);
        var date     = new DateOnly(2026, 5, 15);

        // The same posting appears twice (two sources returned it)
        var postings = new[]
        {
            MakePosting(company: "Acme Corp", title: "Senior Software Engineer",
                        date: date, source: "Dice"),
            MakePosting(company: "Acme Corp", title: "Senior Software Engineer",
                        date: date, source: "USAJOBS"),
        };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Single(result.NewPostings);
        Assert.Equal(1, result.Duplicates);
    }

    // ── Mixed batch ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_CorrectlyCountsAllCategories()
    {
        var existingDate = new DateOnly(2026, 4, 1);
        var existingKeys = new HashSet<(string, string, DateOnly)>
        {
            ("existing co", "senior software engineer", existingDate)
        };

        var mock     = MockSheets(existingKeys);
        var pipeline = BuildPipeline(mock);

        var postings = new[]
        {
            // ✓ New qualifying posting
            MakePosting(company: "New Co", title: "Senior Software Engineer",
                description: "10+ years of experience required.",
                date: new DateOnly(2026, 5, 1)),

            // ✓ New flagged posting (no experience stated)
            MakePosting(company: "Another Co", title: "Lead Platform Engineer",
                description: "Great opportunity for experienced engineers.",
                date: new DateOnly(2026, 5, 2)),

            // ✗ Role filtered
            MakePosting(company: "Corp", title: "Product Manager",
                date: new DateOnly(2026, 5, 3)),

            // ✗ Experience filtered
            MakePosting(company: "Startup", title: "Software Engineer",
                description: "2+ years of experience.",
                date: new DateOnly(2026, 5, 4)),

            // ✗ Duplicate (matches existing sheet row)
            MakePosting(company: "Existing Co", title: "Senior Software Engineer",
                date: existingDate),
        };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Equal(2, result.NewPostings.Count);
        Assert.Equal(1, result.RoleFiltered);
        Assert.Equal(1, result.ExperienceFiltered);
        Assert.Equal(1, result.Duplicates);
        Assert.Equal(5, result.TotalProcessed);
    }

    // ── TotalProcessed ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_TotalProcessedMatchesSumOfAllCategories()
    {
        var mock     = MockSheets();
        var pipeline = BuildPipeline(mock);

        var postings = new[]
        {
            MakePosting(title: "Senior Software Engineer",
                description: "10+ years required."),
            MakePosting(title: "Director of Engineering"),      // role filtered
            MakePosting(title: "Software Engineer",
                description: "2 years of experience."),         // experience filtered
        };

        var result = await pipeline.ProcessAsync(postings);

        Assert.Equal(
            result.NewPostings.Count + result.RoleFiltered + result.ExperienceFiltered + result.Duplicates,
            result.TotalProcessed);
    }

    // ── Empty input ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_HandlesEmptyInput()
    {
        var mock     = MockSheets();
        var pipeline = BuildPipeline(mock);

        var result = await pipeline.ProcessAsync([]);

        Assert.Empty(result.NewPostings);
        Assert.Equal(0, result.TotalProcessed);
    }
}

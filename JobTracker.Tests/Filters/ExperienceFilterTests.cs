using JobTracker.Core.Filters;
using JobTracker.Core.Models;

namespace JobTracker.Tests.Filters;

public class ExperienceFilterTests
{
    private readonly ExperienceFilter _filter = new();

    // ── Senior (8+ years) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Requires 8+ years of experience in software development.")]
    [InlineData("Minimum 10 years of experience required.")]
    [InlineData("At least 8 years of software engineering experience.")]
    [InlineData("We need someone with 12 years of experience.")]
    [InlineData("Minimum of 9 years of relevant professional experience.")]
    public void Classify_ReturnsSenior_WhenDescriptionStatesEightOrMoreYears(string description)
        => Assert.Equal(ExperienceLevel.Senior,
               _filter.Classify(description, "Software Engineer"));

    [Fact]
    public void Classify_ReturnsSenior_WhenWrittenNumberIsEightOrMore()
        => Assert.Equal(ExperienceLevel.Senior,
               _filter.Classify("Requires eight or more years of experience.", "Software Engineer"));

    [Fact]
    public void Classify_ReturnsSenior_WhenHighestValueMeetsThreshold()
    {
        // 3 years is a per-skill requirement; 10 years is the overall bar.
        const string description =
            "3 years of experience with React. " +
            "10 years of overall software engineering experience required.";
        Assert.Equal(ExperienceLevel.Senior, _filter.Classify(description, "Senior Software Engineer"));
    }

    [Fact]
    public void Classify_ReturnsSenior_WhenRangeLowerBoundMeetsThreshold()
        => Assert.Equal(ExperienceLevel.Senior,
               _filter.Classify("8-12 years of software development experience.", "Senior Developer"));

    [Fact]
    public void Classify_ReturnsSenior_ForSeniorKeywordWhenNoYearsStated()
        => Assert.Equal(ExperienceLevel.Senior,
               _filter.Classify("Join our team.", "Senior Software Engineer"));

    // ── Mid (3-7 years) ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("Requires 3+ years of experience.")]
    [InlineData("Minimum 5 years of software engineering experience.")]
    [InlineData("At least 7 years of experience required.")]
    [InlineData("4 to 6 years of experience preferred.")]
    public void Classify_ReturnsMid_WhenDescriptionStatesThreeToSevenYears(string description)
        => Assert.Equal(ExperienceLevel.Mid,
               _filter.Classify(description, "Software Engineer"));

    [Fact]
    public void Classify_ReturnsMid_WhenRangeLowerBoundBelowThreshold()
        => Assert.Equal(ExperienceLevel.Mid,
               _filter.Classify("3-7 years of experience required.", "Software Developer"));

    [Fact]
    public void Classify_ReturnsMid_ForMidKeywordWhenNoYearsStated()
        => Assert.Equal(ExperienceLevel.Mid,
               _filter.Classify("Looking for a mid-level developer.", "Software Engineer"));

    // ── Entry (0-2 years) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Entry level position.", "Software Engineer")]
    [InlineData("Junior developers welcome.", "Junior Software Engineer")]
    [InlineData("New grad opportunity.", "Software Engineer")]
    public void Classify_ReturnsEntry_ForEntrySignals(string description, string title)
        => Assert.Equal(ExperienceLevel.Entry, _filter.Classify(description, title));

    [Theory]
    [InlineData("Requires 1 year of experience.")]
    [InlineData("0-2 years of experience required.")]
    public void Classify_ReturnsEntry_WhenDescriptionStatesZeroToTwoYears(string description)
        => Assert.Equal(ExperienceLevel.Entry,
               _filter.Classify(description, "Software Engineer"));

    // ── Manager ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Engineering Manager")]
    [InlineData("Director of Engineering")]
    [InlineData("VP of Engineering")]
    [InlineData("Head of Engineering")]
    [InlineData("Technical Program Manager")]
    [InlineData("CTO")]
    public void Classify_ReturnsManager_ForManagerTitles(string title)
        => Assert.Equal(ExperienceLevel.Manager,
               _filter.Classify("Lead a team of engineers.", title));

    // ── Unknown ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("We are looking for a talented engineer.", "Software Engineer")]
    [InlineData("Strong proficiency with cloud technologies required.", "Cloud Engineer")]
    [InlineData("", "Software Engineer")]
    public void Classify_ReturnsUnknown_WhenNoSignalFound(string description, string title)
        => Assert.Equal(ExperienceLevel.Unknown, _filter.Classify(description, title));

    // ── IsManagerTitle ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Engineering Manager",    true)]
    [InlineData("Director of Software",   true)]
    [InlineData("VP of Engineering",      true)]
    [InlineData("CTO",                    true)]
    [InlineData("Senior Software Engineer", false)]
    [InlineData("Software Engineer",        false)]
    [InlineData("",                         false)]
    public void IsManagerTitle_ReturnsExpected(string title, bool expected)
        => Assert.Equal(expected, ExperienceFilter.IsManagerTitle(title));

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_IgnoresImplausiblyHighYearValues()
    {
        // "100 years" (company history) should not classify as Senior.
        // Only the 5-year requirement should be seen.
        const string description =
            "Our company was founded 100 years ago. " +
            "We require 5 years of software engineering experience.";
        Assert.Equal(ExperienceLevel.Mid, _filter.Classify(description, "Software Engineer"));
    }
}

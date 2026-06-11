using JobTracker.Core.Filters;

namespace JobTracker.Tests.Filters;

public class RoleFilterTests
{
    private readonly RoleFilter _filter = new();

    // ── Core software engineering titles ─────────────────────────────────────

    [Theory]
    [InlineData("Senior Software Engineer")]
    [InlineData("software developer")]                          // lower-case
    [InlineData("Staff Software Architect")]
    [InlineData("Application Developer III")]
    [InlineData("Application Engineer")]
    [InlineData("APPLICATION DEVELOPER")]                      // upper-case
    public void Qualifies_ReturnsTrue_ForCoreSoftwareTitles(string title)
        => Assert.True(_filter.Qualifies(title));

    // ── Full-stack variants ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Full Stack Developer")]
    [InlineData("Fullstack Engineer")]
    [InlineData("Full-Stack Software Engineer")]
    [InlineData("Senior Frontend Engineer")]
    [InlineData("Front-End Engineer")]
    [InlineData("Backend Engineer")]
    [InlineData("Back-End Software Engineer")]
    public void Qualifies_ReturnsTrue_ForFullStackVariants(string title)
        => Assert.True(_filter.Qualifies(title));

    // ── Adjacent roles ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("Senior DevOps Engineer")]
    [InlineData("Site Reliability Engineer")]
    [InlineData("SRE")]
    [InlineData("Lead Platform Engineer")]
    [InlineData("Principal Data Engineer")]
    [InlineData("Senior Cloud Engineer")]
    [InlineData("Infrastructure Engineer")]
    [InlineData("Systems Engineer")]
    [InlineData("Solutions Architect")]
    [InlineData("ML Engineer")]
    [InlineData("Senior Machine Learning Engineer")]
    public void Qualifies_ReturnsTrue_ForAdjacentRoles(string title)
        => Assert.True(_filter.Qualifies(title));

    // ── Non-qualifying titles ────────────────────────────────────────────────

    [Theory]
    [InlineData("Product Manager")]
    [InlineData("UX Designer")]
    [InlineData("Data Analyst")]
    [InlineData("Business Analyst")]
    [InlineData("Technical Recruiter")]
    [InlineData("Project Manager")]
    [InlineData("Scrum Master")]
    [InlineData("QA Tester")]
    [InlineData("Sales Engineer")]
    [InlineData("")]
    [InlineData("   ")]
    public void Qualifies_ReturnsFalse_ForNonQualifyingTitles(string title)
        => Assert.False(_filter.Qualifies(title));

    // ── Leading exclusion prefixes ───────────────────────────────────────────

    [Theory]
    [InlineData("Manager of Software Engineering")]
    [InlineData("Director of Engineering")]
    [InlineData("VP of Software Development")]
    [InlineData("VP, Engineering")]
    [InlineData("Vice President of Platform Engineering")]
    [InlineData("Head of Software Engineering")]
    [InlineData("Chief Software Architect")]
    [InlineData("Recruiter — Engineering Team")]
    public void Qualifies_ReturnsFalse_WhenLeadingExclusionPrefixPresent(string title)
        => Assert.False(_filter.Qualifies(title));

    // ── Engineering Manager edge case ────────────────────────────────────────
    // "Engineering Manager" does NOT start with a leading exclusion prefix,
    // but also doesn't contain a qualifying keyword — correctly excluded.

    [Fact]
    public void Qualifies_ReturnsFalse_ForEngineeringManager()
        => Assert.False(_filter.Qualifies("Engineering Manager"));

    // ── User-configured keywords ─────────────────────────────────────────────

    [Fact]
    public void Qualifies_ReturnsTrue_WhenExtraIncludeKeywordMatches()
    {
        var filter = new RoleFilter(extraInclude: ["embedded systems"]);
        Assert.True(filter.Qualifies("Embedded Systems Developer"));
    }

    [Fact]
    public void Qualifies_ReturnsFalse_WhenExtraExcludeKeywordMatches()
    {
        var filter = new RoleFilter(extraExclude: ["intern"]);
        Assert.False(filter.Qualifies("Software Engineer Intern"));
    }

    [Fact]
    public void Qualifies_ExtraExcludeTakesPriorityOverBuiltInQualifyingKeyword()
    {
        var filter = new RoleFilter(extraExclude: ["contractor"]);
        Assert.False(filter.Qualifies("Software Engineer — Contractor"));
    }

    [Fact]
    public void Qualifies_ExtraExcludeTakesPriorityOverExtraInclude()
    {
        // Include "security engineer" but also exclude "junior" — exclude wins.
        var filter = new RoleFilter(
            extraInclude: ["security engineer"],
            extraExclude: ["junior"]);
        Assert.False(filter.Qualifies("Junior Security Engineer"));
    }

    [Fact]
    public void Qualifies_ExtraIncludeKeywordsAreCaseInsensitive()
    {
        var filter = new RoleFilter(extraInclude: ["Embedded Systems"]);
        Assert.True(filter.Qualifies("embedded systems developer"));
    }

    // ── Null safety ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_AcceptsNullParameters()
    {
        var filter = new RoleFilter(null, null);
        Assert.True(filter.Qualifies("Senior Software Engineer"));
    }
}

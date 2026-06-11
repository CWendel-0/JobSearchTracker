namespace JobTracker.Core.Models;

public class AppSettings
{
    // ── USAJOBS ───────────────────────────────────────────────────────────────
    public string UsaJobsApiKey { get; set; } = string.Empty;
    public string UsaJobsEmail  { get; set; } = string.Empty;

    // ── Adzuna ────────────────────────────────────────────────────────────────
    /// <summary>App ID from developer.adzuna.com (free registration).</summary>
    public string AdzunaAppId  { get; set; } = string.Empty;
    /// <summary>API key from developer.adzuna.com (free registration).</summary>
    public string AdzunaApiKey { get; set; } = string.Empty;

    // ── The Muse ──────────────────────────────────────────────────────────────
    /// <summary>API key from www.themuse.com/developers/api (free registration).</summary>
    public string TheMuseApiKey { get; set; } = string.Empty;

    // ── Google Sheets ─────────────────────────────────────────────────────────
    public string ServiceAccountKeyPath { get; set; } = string.Empty;
    public string SpreadsheetId         { get; set; } = string.Empty;

    // ── Experience level filter ──────────────────────────────────────────────────
    /// <summary>
    /// Which experience levels to include in fetch results.
    /// Postings whose level cannot be determined (Unknown) are always included
    /// and flagged for manual review regardless of this setting.
    /// Defaults to Senior only to preserve original behaviour.
    /// </summary>
    public List<ExperienceLevel> EnabledExperienceLevels { get; set; } =
        [ExperienceLevel.Senior];

    // ── Search settings ──────────────────────────────────────────────────────────
    /// <summary>
    /// Only include postings published within this many days.
    /// Applied as a post-fetch filter in the pipeline. Default: 14.
    /// Set to 0 to disable date filtering entirely.
    /// </summary>
    public int DaysBack { get; set; } = 14;

    // ── Source toggles ────────────────────────────────────────────────────────
    public SourceSettings Sources { get; set; } = new();

    // ── Keyword overrides ─────────────────────────────────────────────────────
    public List<string> ExtraIncludeKeywords { get; set; } = [];
    public List<string> ExtraExcludeKeywords { get; set; } = [];
}

public class SourceSettings
{
    // Free, no auth required
    public bool UsaJobs          { get; set; } = true;
    // Dice: public API shut down 2017; no reliable endpoint available.
    // public bool Dice { get; set; } = false;
    public bool RemoteJobsFinder { get; set; } = true;
    public bool Remotive         { get; set; } = true;
    public bool RemoteOk         { get; set; } = true;
    public bool Himalayas        { get; set; } = true;
    public bool Arbeitnow        { get; set; } = true;
    public bool WeWorkRemotely   { get; set; } = true;
    public bool WorkingNomads    { get; set; } = true;

    // Require API key registration (defaulted off until configured)
    public bool Adzuna  { get; set; } = false;
    public bool TheMuse { get; set; } = false;
}

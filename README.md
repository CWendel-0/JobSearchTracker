# Job Application Tracker

A Windows desktop application that fetches remote software engineering job
postings from multiple job boards, filters them by role and experience level,
deduplicates them against postings already in your Google Sheet, and lets you
review and approve results before they are written.

---

## Prerequisites

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- A Google account (free Gmail is fine)

---

## First-Time Setup

### 1 — Build the project

```powershell
cd path\to\JobTracker
dotnet build
```

### 2 — Set up Google Sheets access

The app writes to a Google Sheet using a free service account.

**a. Create a Google Cloud project**
1. Go to [console.cloud.google.com](https://console.cloud.google.com) and sign
   in with your Gmail
2. Click the project dropdown → **New Project** → name it `JobTracker` → **Create**

**b. Enable the Sheets API**
1. Go to **APIs & Services → Library**
2. Search for **Google Sheets API** → click it → **Enable**

**c. Create a service account**
1. Go to **APIs & Services → Credentials**
2. Click **+ Create Credentials → Service Account**
3. Name it `jobtracker-sheets` → **Create and Continue**
4. Skip the optional steps → **Done**

**d. Download the JSON key**
1. On the Credentials page, click your new service account
2. Go to the **Keys** tab → **Add Key → Create new key → JSON → Create**
3. A `.json` file downloads — move it somewhere permanent, e.g.:
   `C:\Secrets\jobtracker-service-account.json`

**e. Share your Google Sheet**
1. Open the downloaded JSON in Notepad and find `"client_email"` — it looks
   like `jobtracker-sheets@jobtracker-123456.iam.gserviceaccount.com`
2. Open your Google Sheet → **Share** → paste the email → set role to
   **Editor** → **Send**

**f. Get your Spreadsheet ID**
Copy the long string from your sheet's URL:
`https://docs.google.com/spreadsheets/d/`**`1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms`**`/edit`

### 3 — Get a USAJOBS API key (free, instant)

1. Go to [developer.usajobs.gov/apirequest](https://developer.usajobs.gov/apirequest)
2. Fill in your name, email, and a User-Agent string (e.g. `JobTracker`)
3. Your API key arrives by email within seconds

### 4 — Configure the app

Run the app and click **⚙ Settings**:

| Tab | Field | Value |
|-----|-------|-------|
| Google Sheets | Service Account Key File | Full path to your `.json` key file |
| Google Sheets | Spreadsheet ID | The ID from your sheet URL |
| Sources | USAJOBS API Key | Key from the email in step 3 |
| Sources | USAJOBS Registration Email | Email you used to register |
| Filters | Date Range | Days back to search (default: 14) |

Click **Save**.

### 5 — First run

Click **▶ Fetch New Jobs**. The app will:
1. Query all enabled sources concurrently
2. Filter results by role keywords and experience level
3. Deduplicate against jobs already in your sheet
4. Present new results for review

Check the rows you want to keep, uncheck any you don't, then click
**Add Selected to Sheet**.

---

## Sources

| Source | Auth | Notes |
|--------|------|-------|
| USAJOBS | API key (free) | Official federal jobs API |
| Dice | None | Public JSON endpoint |
| Remotive | None | Remote-only, free public API |
| Remote OK | None | Remote tech jobs JSON feed |
| Himalayas | None | Remote tech focused |
| Arbeitnow | None | Free public API |
| We Work Remotely | None | RSS feed (3 categories) |
| Working Nomads | None | Public API |
| RemoteJobsFinder | None | HTML scrape |
| Adzuna | App ID + key (free) | Large aggregator, register at developer.adzuna.com |
| The Muse | API key (free) | Tech companies, register at themuse.com/developers |

Sources that require API keys default to **off** in Settings until you add
credentials. All other sources default to **on**.

---

## Filtering

**Role filter** — a posting qualifies if its title contains any of these
keywords (case-insensitive):

- Core: `software engineer`, `software developer`, `software architect`,
  `full stack`, `frontend engineer`, `backend engineer`, `application developer`
- Adjacent: `devops engineer`, `site reliability engineer`, `sre`,
  `platform engineer`, `data engineer`, `cloud engineer`,
  `infrastructure engineer`, `systems engineer`, `solutions architect`,
  `ml engineer`, `machine learning engineer`

Titles beginning with `Manager`, `Director`, `VP`, `Head of`, `Chief`,
`Recruiter` are excluded even when they contain a qualifying keyword.

Add extra include or exclude keywords in **Settings → Filters**.

**Experience filter** — descriptions stating fewer than 8 years of experience
are excluded. Descriptions stating 8 or more years are included. Descriptions
with no explicit year requirement are included but flagged with ⚠ in the
results table for manual review.

**Date filter** — only postings published within the configured number of days
are shown (default: 14). Set to 0 in Settings to disable.

---

## Spreadsheet Layout

| Col | Field | Managed by |
|-----|-------|-----------|
| A | Company Name | App |
| B | Job Title | App |
| C | Job Posting URL | App |
| D | Posting Date | App |
| E | Source | App |
| F | Applied? | You (checkbox) |
| G | Application Date | Apps Script (auto) |
| H | Received Response? | You (checkbox) |
| I | Response Date | Apps Script (auto) |
| J | Received Interview? | You (checkbox) |
| K | Interview Date | Apps Script (auto) |
| L | Contact Information | App |

The auto-date columns (G, I, K) fill in automatically when you tick the
corresponding checkbox, handled by the Google Apps Script attached to the sheet.

---

## Logs

Logs are written to:
```
%APPDATA%\JobTracker\logs\jobtracker-YYYYMMDD.log
```

Seven daily log files are kept. Each fetch run logs the sources queried, result
counts, and any per-source errors. Sheet write operations and unhandled
exceptions are also recorded.

---

## Settings File

Settings are stored at:
```
%APPDATA%\JobTracker\appsettings.json
```

This file is never committed to source control. Delete it to reset all settings
to defaults.

---

## Project Structure

```
JobTracker/
├── JobTracker.Core/          # Business logic — no UI dependency
│   ├── Models/               # JobPosting, AppSettings, ExperienceFlag
│   ├── Fetchers/             # One fetcher per job board
│   ├── Filters/              # RoleFilter, ExperienceFilter
│   ├── Pipeline.cs           # Orchestration: filter → dedup → result
│   ├── SheetsClient.cs       # Google Sheets read/write
│   ├── HttpRetryHandler.cs   # Exponential back-off for HTTP calls
│   └── SettingsManager.cs    # Load/save appsettings.json
│
├── JobTracker.App/           # WPF desktop application
│   ├── ViewModels/           # MainViewModel, JobResultViewModel
│   ├── App.xaml/.cs          # Startup, logging, unhandled exceptions
│   ├── MainWindow.xaml/.cs   # Main results UI
│   └── SettingsWindow.xaml/.cs
│
└── JobTracker.Tests/         # xUnit unit tests
    ├── Filters/              # RoleFilterTests, ExperienceFilterTests
    ├── Fetchers/             # FetcherParserTests (no live HTTP)
    └── PipelineTests.cs
```

---

## Running Tests

```powershell
dotnet test
```

All tests are self-contained and require no network access or credentials.

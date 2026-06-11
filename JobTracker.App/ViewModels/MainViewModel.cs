using System.Collections.ObjectModel;
using Serilog;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core;
using JobTracker.Core.Fetchers;
using JobTracker.Core.Filters;
using JobTracker.Core.Models;

namespace JobTracker.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ── Observable state ──────────────────────────────────────────────────────

    /// <summary>True while a fetch or sheet-write operation is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddToSheetCommand))]
    private bool _isFetching;

    /// <summary>One-line status shown in the toolbar.</summary>
    [ObservableProperty]
    private string _statusText = "Ready — open Settings before the first run.";

    /// <summary>Summary counts shown in the footer after a fetch.</summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>Number of rows currently checked in the results table.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddToSheetCommand))]
    [NotifyPropertyChangedFor(nameof(AddToSheetButtonText))]
    private int _selectedCount;

    /// <summary>True once a fetch has returned at least one result row.</summary>
    [ObservableProperty]
    private bool _hasResults;

    /// <summary>Job postings returned by the last fetch, presented for review.</summary>
    public ObservableCollection<JobResultViewModel> Results { get; } = [];

    /// <summary>Button label that includes the live selected-row count.</summary>
    public string AddToSheetButtonText => SelectedCount > 0
        ? $"Add Selected to Sheet  ({SelectedCount})"
        : "Add Selected to Sheet";

    private CancellationTokenSource? _cts;

    // ── Fetch ─────────────────────────────────────────────────────────────────

    private bool CanFetch() => !IsFetching;

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        var settings = SettingsManager.Load();

        if (!SettingsManager.IsConfigured(settings))
        {
            MessageBox.Show(
                "Please configure the Google service account path and Spreadsheet ID " +
                "in Settings before fetching.",
                "Configuration required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            OpenSettings();
            return;
        }

        IsFetching    = true;
        StatusText    = "Fetching jobs…";
        SummaryText   = string.Empty;
        SelectedCount = 0;
        HasResults    = false;
        Results.Clear();

        _cts = new CancellationTokenSource();

        try
        {
            using var http = HttpRetryHandler.CreateClient();
            var fetchers = BuildFetchers(settings, http);

            if (!fetchers.Any())
            {
                StatusText = "No sources enabled — check Settings.";
                return;
            }

            var sourceNames = string.Join(", ", fetchers.Select(f => f.SourceName));
            Log.Information("Fetch started — sources: {Sources}, DaysBack: {Days}", sourceNames, settings.DaysBack);

            var sheets   = new SheetsClient(settings.ServiceAccountKeyPath, settings.SpreadsheetId);
            var pipeline = new Pipeline(
                sheets,
                new RoleFilter(settings.ExtraIncludeKeywords,
                               settings.ExtraExcludeKeywords,
                               settings.EnabledExperienceLevels),
                new ExperienceFilter(),
                settings.DaysBack,
                settings.EnabledExperienceLevels);

            var result = await pipeline.RunAsync(fetchers, _cts.Token);

            Log.Information(
                "Fetch complete — new: {New}, duplicates: {Dups}, date-filtered: {Date}, role-filtered: {Role}, exp-filtered: {Exp}",
                result.NewPostings.Count, result.Duplicates,
                result.DateFiltered, result.RoleFiltered, result.ExperienceFiltered);

            foreach (var err in result.FetcherErrors)
                Log.Error(err.Exception, "Fetcher {Source} failed: {Message}", err.SourceName, err.Message);

            foreach (var posting in result.NewPostings)
            {
                var vm = new JobResultViewModel(posting);
                vm.PropertyChanged += (_, _) => RefreshSelectedCount();
                Results.Add(vm);
            }

            HasResults    = Results.Count > 0;
            SelectedCount = Results.Count(r => r.IsSelected);
            SummaryText   = BuildSummary(result);

            if (result.FetcherErrors.Count > 0)
            {
                var names  = string.Join(", ", result.FetcherErrors.Select(e => e.SourceName));
                StatusText = $"Done with errors from: {names}  — {DateTime.Now:HH:mm}";
            }
            else
            {
                StatusText = $"Done — {DateTime.Now:HH:mm}";
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Fetch cancelled by user");
            StatusText = "Fetch cancelled.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fetch failed");
            StatusText = $"Error: {ex.Message}";
            MessageBox.Show(
                $"The fetch failed with an unexpected error:\n\n{ex.Message}",
                "Fetch error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsFetching = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ── Add to sheet ──────────────────────────────────────────────────────────

    private bool CanAddToSheet() => !IsFetching && SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanAddToSheet))]
    private async Task AddToSheetAsync()
    {
        var settings = SettingsManager.Load();
        var toWrite  = Results.Where(r => r.IsSelected).Select(r => r.Posting).ToList();

        IsFetching = true;
        StatusText = $"Writing {toWrite.Count} row{(toWrite.Count == 1 ? "" : "s")} to sheet…";

        try
        {
            var sheets = new SheetsClient(settings.ServiceAccountKeyPath, settings.SpreadsheetId);
            Log.Information("Writing {Count} rows to sheet", toWrite.Count);
            await sheets.AppendRowsAsync(toWrite);
            Log.Information("Sheet write complete");

            // Remove written rows from the review list.
            foreach (var vm in Results.Where(r => r.IsSelected).ToList())
                Results.Remove(vm);

            RefreshSelectedCount();
            HasResults  = Results.Count > 0;
            SummaryText = Results.Count > 0 ? $"{Results.Count} remaining in review" : string.Empty;
            StatusText  = $"Added {toWrite.Count} job{(toWrite.Count == 1 ? "" : "s")} to sheet — {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sheet write failed");
            StatusText = $"Sheet write failed: {ex.Message}";
            MessageBox.Show(
                $"Could not write to the sheet:\n\n{ex.Message}",
                "Sheet write error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsFetching = false;
        }
    }

    // ── Select / Deselect all ─────────────────────────────────────────────────

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var r in Results) r.IsSelected = true;
        RefreshSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var r in Results) r.IsSelected = false;
        RefreshSelectedCount();
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenSettings()
    {
        var win = new SettingsWindow { Owner = Application.Current.MainWindow };
        win.ShowDialog();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void RefreshSelectedCount() =>
        SelectedCount = Results.Count(r => r.IsSelected);

    private static List<IJobFetcher> BuildFetchers(AppSettings settings, HttpClient http)
    {
        var fetchers = new List<IJobFetcher>();

        // ── Free, no auth ──────────────────────────────────────────────────
        if (settings.Sources.UsaJobs &&
            !string.IsNullOrWhiteSpace(settings.UsaJobsApiKey))
            fetchers.Add(new UsaJobsFetcher(http, settings.UsaJobsApiKey, settings.UsaJobsEmail));

        // Dice: removed — public API shut down 2017, no reliable endpoint.

        if (settings.Sources.RemoteJobsFinder)
            fetchers.Add(new RemoteJobsFinderFetcher(http));

        if (settings.Sources.Remotive)
            fetchers.Add(new RemotiveFetcher(http));

        if (settings.Sources.RemoteOk)
            fetchers.Add(new RemoteOkFetcher(http));

        if (settings.Sources.Himalayas)
            fetchers.Add(new HimalayasFetcher(http));

        if (settings.Sources.Arbeitnow)
            fetchers.Add(new ArbeitnowFetcher(http));

        if (settings.Sources.WeWorkRemotely)
            fetchers.Add(new WeWorkRemotelyFetcher(http));

        if (settings.Sources.WorkingNomads)
            fetchers.Add(new WorkingNomadsFetcher(http));

        // ── Require API key ────────────────────────────────────────────────
        if (settings.Sources.Adzuna &&
            !string.IsNullOrWhiteSpace(settings.AdzunaAppId) &&
            !string.IsNullOrWhiteSpace(settings.AdzunaApiKey))
            fetchers.Add(new AdzunaFetcher(http, settings.AdzunaAppId, settings.AdzunaApiKey));

        if (settings.Sources.TheMuse &&
            !string.IsNullOrWhiteSpace(settings.TheMuseApiKey))
            fetchers.Add(new TheMuseFetcher(http, settings.TheMuseApiKey));

        return fetchers;
    }

    private static string BuildSummary(PipelineResult result)
    {
        var parts = new List<string>();

        if (result.NewPostings.Count > 0)
            parts.Add($"{result.NewPostings.Count} new");

        if (result.Duplicates > 0)
            parts.Add($"{result.Duplicates} duplicate{(result.Duplicates == 1 ? "" : "s")} skipped");

        if (result.ExperienceFiltered > 0)
            parts.Add($"{result.ExperienceFiltered} excluded by experience level");

        if (result.DateFiltered > 0)
            parts.Add($"{result.DateFiltered} outside date window");

        if (result.RoleFiltered > 0)
            parts.Add($"{result.RoleFiltered} role-filtered");

        return parts.Count > 0
            ? string.Join("  ·  ", parts)
            : "No new jobs found matching your filters.";
    }
}

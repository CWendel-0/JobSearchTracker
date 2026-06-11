using System.IO;
using System.Windows;
using Microsoft.Win32;
using JobTracker.Core;
using JobTracker.Core.Models;
using JobTracker.Core.Filters;

namespace JobTracker.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = SettingsManager.Load();

        // Google Sheets
        ServiceAccountPathBox.Text = s.ServiceAccountKeyPath;
        SpreadsheetIdBox.Text      = s.SpreadsheetId;

        // Sources — free
        UsaJobsCheck.IsChecked          = s.Sources.UsaJobs;
        RemoteJobsFinderCheck.IsChecked = s.Sources.RemoteJobsFinder;
        RemotiveCheck.IsChecked         = s.Sources.Remotive;
        RemoteOkCheck.IsChecked         = s.Sources.RemoteOk;
        HimalayasCheck.IsChecked        = s.Sources.Himalayas;
        ArbeitnowCheck.IsChecked        = s.Sources.Arbeitnow;
        WeWorkRemotelyCheck.IsChecked   = s.Sources.WeWorkRemotely;
        WorkingNomadsCheck.IsChecked    = s.Sources.WorkingNomads;

        // Sources — API key
        UsaJobsApiKeyBox.Text  = s.UsaJobsApiKey;
        UsaJobsEmailBox.Text   = s.UsaJobsEmail;
        AdzunaCheck.IsChecked  = s.Sources.Adzuna;
        AdzunaAppIdBox.Text    = s.AdzunaAppId;
        AdzunaApiKeyBox.Text   = s.AdzunaApiKey;
        TheMuseCheck.IsChecked = s.Sources.TheMuse;
        TheMuseApiKeyBox.Text  = s.TheMuseApiKey;

        // Experience levels
        var levels = s.EnabledExperienceLevels.ToHashSet();
        EntryLevelCheck.IsChecked   = levels.Contains(ExperienceLevel.Entry);
        MidLevelCheck.IsChecked     = levels.Contains(ExperienceLevel.Mid);
        SeniorLevelCheck.IsChecked  = levels.Contains(ExperienceLevel.Senior);
        ManagerLevelCheck.IsChecked = levels.Contains(ExperienceLevel.Manager);

        // Filters
        DaysBackBox.Text = s.DaysBack.ToString();
        ExtraIncludeBox.Text = string.Join(Environment.NewLine, s.ExtraIncludeKeywords);
        ExtraExcludeBox.Text = string.Join(Environment.NewLine, s.ExtraExcludeKeywords);
    }

    private void BrowseServiceAccount_Click(object sender, RoutedEventArgs e)
    {
        var current = ServiceAccountPathBox.Text.Trim();
        var initial = !string.IsNullOrEmpty(current) && File.Exists(current)
            ? Path.GetDirectoryName(current) ?? string.Empty
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var dialog = new OpenFileDialog
        {
            Title            = "Select Service Account JSON Key File",
            Filter           = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = initial,
        };

        if (dialog.ShowDialog() == true)
            ServiceAccountPathBox.Text = dialog.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        static List<string> ParseLines(string text) =>
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries |
                              StringSplitOptions.TrimEntries).ToList();

        var settings = new AppSettings
        {
            // Google Sheets
            ServiceAccountKeyPath = ServiceAccountPathBox.Text.Trim(),
            SpreadsheetId         = SpreadsheetIdBox.Text.Trim(),

            // API keys
            UsaJobsApiKey  = UsaJobsApiKeyBox.Text.Trim(),
            UsaJobsEmail   = UsaJobsEmailBox.Text.Trim(),
            AdzunaAppId    = AdzunaAppIdBox.Text.Trim(),
            AdzunaApiKey   = AdzunaApiKeyBox.Text.Trim(),
            TheMuseApiKey  = TheMuseApiKeyBox.Text.Trim(),

            Sources = new SourceSettings
            {
                UsaJobs          = UsaJobsCheck.IsChecked          == true,
                RemoteJobsFinder = RemoteJobsFinderCheck.IsChecked == true,
                Remotive         = RemotiveCheck.IsChecked         == true,
                RemoteOk         = RemoteOkCheck.IsChecked         == true,
                Himalayas        = HimalayasCheck.IsChecked        == true,
                Arbeitnow        = ArbeitnowCheck.IsChecked        == true,
                WeWorkRemotely   = WeWorkRemotelyCheck.IsChecked   == true,
                WorkingNomads    = WorkingNomadsCheck.IsChecked    == true,
                Adzuna           = AdzunaCheck.IsChecked           == true,
                TheMuse          = TheMuseCheck.IsChecked          == true,
            },

            EnabledExperienceLevels = new List<ExperienceLevel>(
                new[]
                {
                    (EntryLevelCheck.IsChecked   == true, ExperienceLevel.Entry),
                    (MidLevelCheck.IsChecked     == true, ExperienceLevel.Mid),
                    (SeniorLevelCheck.IsChecked  == true, ExperienceLevel.Senior),
                    (ManagerLevelCheck.IsChecked == true, ExperienceLevel.Manager),
                }
                .Where(t => t.Item1).Select(t => t.Item2)),

            DaysBack = int.TryParse(DaysBackBox.Text.Trim(), out var d) && d >= 0 ? d : 14,

            ExtraIncludeKeywords = ParseLines(ExtraIncludeBox.Text),
            ExtraExcludeKeywords = ParseLines(ExtraExcludeBox.Text),
        };

        SettingsManager.Save(settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

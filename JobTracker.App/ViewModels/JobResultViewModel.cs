using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Core.Models;

namespace JobTracker.App.ViewModels;

/// <summary>
/// Wraps a <see cref="JobPosting"/> for display in the results DataGrid.
/// Adds the per-row checkbox state (<see cref="IsSelected"/>) that the user
/// toggles before committing rows to the spreadsheet.
/// </summary>
public partial class JobResultViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public JobPosting Posting { get; }

    public string   Company             => Posting.Company;
    public string   Title               => Posting.Title;
    public string   Source              => Posting.Source;
    public string   Url                 => Posting.Url;
    public string   PostingDateDisplay  => Posting.PostingDate.ToString("MMM d, yyyy");
    public bool IsFlagged => Posting.Level == ExperienceLevel.Unknown;

    public string LevelDisplay => Posting.Level switch
    {
        ExperienceLevel.Entry   => "Entry",
        ExperienceLevel.Mid     => "Mid",
        ExperienceLevel.Senior  => "Senior",
        ExperienceLevel.Manager => "Manager",
        _                       => "",
    };

    public string FlagTooltip => IsFlagged
        ? "No explicit year requirement found — included for manual review"
        : string.Empty;

    public JobResultViewModel(JobPosting posting)
    {
        Posting = posting;
    }
}

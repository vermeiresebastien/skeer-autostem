using StemForge.Core.Media;
using StemForge.Core.Models;

namespace StemForge.App.ViewModels;

/// <summary>
/// Presentation wrapper around a queue job.
///
/// Everything the row shows is derived from the job itself, so the queue and any counts
/// elsewhere in the window can never disagree. The only external input is the batch's
/// current profile, used to point out a job whose saved settings differ from this batch.
/// </summary>
public sealed class JobRowViewModel : ObservableObject
{
    private readonly Func<JobProfile> _batchProfile;

    public JobRowViewModel(Job job, Func<JobProfile> batchProfile)
    {
        Job = job;
        _batchProfile = batchProfile;
    }

    public Job Job { get; }

    public string Id => Job.Id;

    public string FileName => Job.DisplayName;

    public string FilePath => Job.SourcePath;

    public JobProfile SavedProfile => Job.Profile;

    /// <summary>"All stems · High Speed"</summary>
    public string SettingsSummary =>
        $"{StemsLabel(Job.Stems)} · {Job.Mode.DisplayName()}";

    public string StemsSummary => Job.Stems.Describe();

    /// <summary>True when this job was saved with settings the current batch would not use.</summary>
    public bool SettingsDifferFromBatch
    {
        get
        {
            var batch = _batchProfile();
            return Job.Stems != batch.Stems || Job.Mode != batch.Mode;
        }
    }

    public string BatchSettingsSummary
    {
        get
        {
            var batch = _batchProfile();
            return $"{StemsLabel(batch.Stems)} · {batch.Mode.DisplayName()}";
        }
    }

    // ---------------------------------------------------------------- status

    public string StatusLabel => Job.Status switch
    {
        JobStatus.Pending => "Ready",
        JobStatus.Running => "Processing",
        JobStatus.Completed => "Completed",
        JobStatus.Failed => "Failed",
        JobStatus.Skipped => "Skipped",
        _ => Job.Status.ToString(),
    };

    /// <summary>Lower-case key used by the badge style to pick a colour.</summary>
    public string StatusLevel => Job.Status switch
    {
        JobStatus.Pending => "ready",
        JobStatus.Running => "processing",
        JobStatus.Completed => "completed",
        JobStatus.Failed => "failed",
        JobStatus.Skipped => "skipped",
        _ => "ready",
    };

    // ---------------------------------------------------------------- details

    public bool HasDetails => !string.IsNullOrEmpty(DetailsText);

    /// <summary>
    /// The one line the Details column shows. For a failure this is the actual reason,
    /// because that is the thing the user needs to act on.
    /// </summary>
    public string DetailsText => Job.Status switch
    {
        JobStatus.Failed => FailureText,
        JobStatus.Running => string.IsNullOrEmpty(Job.Stage.DisplayName()) ? "Processing" : Job.Stage.DisplayName(),
        JobStatus.Completed => $"{Job.OutputFiles.Count} of {Job.Stems.Count()} stems verified",
        JobStatus.Skipped => "Already done — verified output still on disk",
        JobStatus.Pending when SettingsDifferFromBatch =>
            $"Saved settings: {SettingsSummary} (this batch is {BatchSettingsSummary})",
        _ => string.Empty,
    };

    /// <summary>Extra guidance shown in the expanded row, or empty.</summary>
    public string DetailsHelp => Job.Status switch
    {
        JobStatus.Failed when Job.ErrorKind == ErrorKind.UnsupportedFormat =>
            $"Supported types: {AudioFileTypes.DisplayList}.",
        JobStatus.Failed when Job.ErrorKind == ErrorKind.SourceMissing =>
            "The file was moved, renamed or deleted after it was added. Add it again from its new location.",
        JobStatus.Failed when Job.PartialFiles.Count > 0 =>
            "The stems listed below passed verification. They are partial output only, so they do not count as a finished result.",
        JobStatus.Pending when SettingsDifferFromBatch =>
            "Pressing Run saves this batch's settings onto every unfinished track.",
        _ => string.Empty,
    };

    private string FailureText
    {
        get
        {
            var reason = Job.Error?.Trim();
            if (string.IsNullOrEmpty(reason)) reason = FallbackReason(Job.ErrorKind);
            return reason;
        }
    }

    /// <summary>True when a failed attempt kept verified partial stems.</summary>
    public bool HasPartial => Job.PartialFiles.Count > 0;

    private static string FallbackReason(ErrorKind kind) => kind switch
    {
        ErrorKind.SourceMissing => "The source file is missing.",
        ErrorKind.SourceChanged => "The source file changed after it was added.",
        ErrorKind.UnsupportedFormat => "Unsupported file type.",
        ErrorKind.LiveCrashed => "Ableton Live closed unexpectedly.",
        ErrorKind.LiveLostFocus => "Ableton Live lost the foreground.",
        ErrorKind.StemsMissing => "Live did not produce any stems.",
        ErrorKind.StemsUnverified => "The stems did not pass verification.",
        ErrorKind.DiskFull => "Not enough free space on the output drive.",
        ErrorKind.DestinationUnwritable => "The output folder is not writable.",
        ErrorKind.SeparationTimeout => "Stem separation timed out.",
        _ => "Processing failed.",
    };

    // ---------------------------------------------------------------- output

    public bool HasResult => Job.Status == JobStatus.Completed && !string.IsNullOrEmpty(Job.ResultFolder);

    /// <summary>Only completed, verified output may be opened as a result.</summary>
    public bool CanOpenResults => HasResult;

    public string? ResultPath => Job.DestinationRoot is null || Job.ResultFolder is null
        ? null
        : Path.Combine(Job.DestinationRoot, Job.ResultFolder);

    public bool CanOpenPartial => Job.PartialFiles.Count > 0 && !string.IsNullOrEmpty(Job.PartialFolder);

    public string? PartialPath => Job.DestinationRoot is null || Job.PartialFolder is null
        ? null
        : Path.Combine(Job.DestinationRoot, Job.PartialFolder);

    public string OutputSummary => Job.OutputFiles.Count == 0
        ? "Not published yet"
        : string.Join(", ", Job.OutputFiles);

    public string PartialSummary => Job.PartialFiles.Count == 0
        ? string.Empty
        : $"{string.Join(", ", Job.PartialFiles)} ({Job.PartialFiles.Count} of {Job.Stems.Count()} verified)";

    // ---------------------------------------------------------------- row state

    private bool _isExpanded;

    /// <summary>Row details panel visibility, toggled by the row's overflow button.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(ExpandGlyph));
        }
    }

    public string ExpandGlyph => IsExpanded ? "Collapse" : "Details";

    public bool CanRemove => Job.Status is JobStatus.Pending or JobStatus.Skipped or JobStatus.Failed;

    public void Refresh()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusLevel));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(DetailsHelp));
        OnPropertyChanged(nameof(HasDetails));
        OnPropertyChanged(nameof(SettingsSummary));
        OnPropertyChanged(nameof(SettingsDifferFromBatch));
        OnPropertyChanged(nameof(BatchSettingsSummary));
        OnPropertyChanged(nameof(CanOpenResults));
        OnPropertyChanged(nameof(CanOpenPartial));
        OnPropertyChanged(nameof(HasPartial));
        OnPropertyChanged(nameof(ResultPath));
        OnPropertyChanged(nameof(PartialPath));
        OnPropertyChanged(nameof(OutputSummary));
        OnPropertyChanged(nameof(PartialSummary));
        OnPropertyChanged(nameof(CanRemove));
    }

    private static string StemsLabel(Stems stems) => stems switch
    {
        Stems.All => "All stems",
        Stems.Drums => "Drums only",
        _ => stems.Describe(),
    };
}

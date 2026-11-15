using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using StemForge.Core.Audio;
using StemForge.Core.Engines;
using StemForge.Core.Live;
using StemForge.Core.Live.Projects;
using StemForge.Core.Logging;
using StemForge.Core.Media;
using StemForge.Core.Models;
using StemForge.Core.Orchestration;
using StemForge.Core.Queue;
using StemForge.Core.Support;

namespace StemForge.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore = new();
    private readonly QueueService _queue;
    private readonly AppSettings _settings;
    private readonly RunLog _log;

    private CancellationTokenSource? _runCancellation;
    private BatchRunner? _runner;

    private string _outputFolder = string.Empty;
    private string _statusMessage = "Ready.";
    private string _statusLevel = "Idle";
    private string _liveStatus = "Ableton Live: not located yet.";
    private string _currentTrack = "-";
    private string _currentStage = "Idle";
    private double _trackPercent;
    private bool _showTrackPercent;
    private bool _isRunning;
    private bool _stopAfterCurrent;
    private string _logPath = string.Empty;

    public MainViewModel()
    {
        AppPaths.EnsureCreated();
        _settings = _settingsStore.Load();
        _queue = new QueueService(new QueueStore());
        _queue.Load();
        _log = new RunLog();
        _logPath = _log.FilePath;

        foreach (var job in _queue.Jobs) Jobs.Add(CreateRow(job));

        // Restore remembered state; High Speed and All four stems are the first-launch defaults.
        _outputFolder = _settings.OutputFolder ?? string.Empty;
        ApplyStemsFromSettings();
        IsHighSpeed = _settings.LastMode == ProcessingMode.HighSpeed;
        IsHighQuality = _settings.LastMode == ProcessingMode.HighQuality;
        IsRecursiveImport = _settings.RecursiveImport;
        IsKeepAwake = _settings.KeepAwakeDuringRun;
        IsSimulatedEngine = _settings.UseSimulatedRunner;

        AddFilesCommand = new RelayCommand(_ => AddFiles());
        AddFolderCommand = new RelayCommand(_ => AddFolder());
        BrowseOutputFolderCommand = new RelayCommand(_ => ChooseOutputFolder());
        LocateLiveCommand = new RelayCommand(_ => LocateLive());
        BrowseLiveCommand = new RelayCommand(_ => BrowseForLive());
        RunCommand = new RelayCommand(_ => _ = RunAsync(), _ => CanRun);
        StopAfterCurrentCommand = new RelayCommand(_ => StopAfterCurrent = true, _ => IsRunning && !StopAfterCurrent);
        RetryFailedCommand = new RelayCommand(_ => RetryFailed(), _ => RetryableCount > 0 && !IsRunning);
        OpenOutputFolderCommand = new RelayCommand(_ => OpenFolder(OutputFolder), _ => Directory.Exists(OutputFolder));
        OpenLogFolderCommand = new RelayCommand(_ => OpenFolder(AppPaths.LogsFolder));
        ClearCompletedCommand = new RelayCommand(_ => ClearCompleted(), _ => CompletedCount + SkippedCount > 0 && !IsRunning);
        ToggleRowDetailsCommand = new RelayCommand(p => ToggleRowDetails(p as JobRowViewModel));
        RemoveRowCommand = new RelayCommand(p => RemoveJob(p as JobRowViewModel));
        OpenRowResultsCommand = new RelayCommand(p => OpenResults(p as JobRowViewModel));
        OpenRowPartialCommand = new RelayCommand(p => OpenPartial(p as JobRowViewModel));

        LogLine(_logPath);
        UpdateLiveStatus();
        Recalculate();
    }

    public ObservableCollection<JobRowViewModel> Jobs { get; } = new();

    public string LogFilePath => _logPath;

    // ------------------------------------------------------------------ commands

    public RelayCommand AddFilesCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand BrowseOutputFolderCommand { get; }
    public RelayCommand LocateLiveCommand { get; }
    public RelayCommand BrowseLiveCommand { get; }
    public RelayCommand RunCommand { get; }
    public RelayCommand StopAfterCurrentCommand { get; }
    public RelayCommand RetryFailedCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand ClearCompletedCommand { get; }
    public RelayCommand ToggleRowDetailsCommand { get; }
    public RelayCommand RemoveRowCommand { get; }
    public RelayCommand OpenRowResultsCommand { get; }
    public RelayCommand OpenRowPartialCommand { get; }

    // ------------------------------------------------------------------ state

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (!Set(ref _outputFolder, value)) return;
            _settings.OutputFolder = string.IsNullOrWhiteSpace(value) ? null : value;
            SaveSettings();
            OnPropertyChanged(nameof(OutputFolderSummary));
            OnPropertyChanged(nameof(HasOutputFolder));
            Recalculate();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (!Set(ref _statusMessage, value)) return;

            // The command bar shows batch state rather than a status line, so transient
            // messages are kept in the run log where they remain diagnosable.
            _log?.Info(value);
        }
    }

    /// <summary>Drives the status indicator colour: "Idle", "Running" or "Error".</summary>
    public string StatusLevel { get => _statusLevel; private set => Set(ref _statusLevel, value); }

    public string LiveStatus { get => _liveStatus; private set => Set(ref _liveStatus, value); }

    /// <summary>Engine label for the header chip, e.g. "Ableton Live 12.3.5 Suite".</summary>
    public string LiveChip
    {
        get
        {
            if (IsSimulatedEngine) return "Simulated engine";
            if (string.IsNullOrWhiteSpace(_settings.LiveVersion)) return "Ableton Live not found";
            return $"Ableton Live {_settings.LiveVersion} {_settings.LiveEdition}".Trim();
        }
    }

    // ------------------------------------------------------------------ diagnostics

    public string LiveExePath => string.IsNullOrWhiteSpace(_settings.LiveExePath)
        ? "Not located yet"
        : _settings.LiveExePath;

    public string WorkingFolderPath => AppPaths.WorkRoot;

    public string AppDataFolder => AppPaths.Root;

    public string SupportedFormats => StemForge.Core.Media.AudioFileTypes.DisplayList;

    public string OutputFolderSummary => string.IsNullOrWhiteSpace(OutputFolder)
        ? "Choose a folder"
        : OutputFolder;

    /// <summary>True once a usable output folder is set, which reveals the Open action.</summary>
    public bool HasOutputFolder => !string.IsNullOrWhiteSpace(OutputFolder) && Directory.Exists(OutputFolder);

    public string CurrentTrack { get => _currentTrack; private set => Set(ref _currentTrack, value); }

    public string CurrentStage { get => _currentStage; private set => Set(ref _currentStage, value); }

    // ------------------------------------------------------------------ batch state
    //
    // Every count is derived from the queue itself, so the command bar can never disagree
    // with the rows the user is looking at - even after a run resets its own counters.

    public int TotalCount => _queue.Jobs.Count;
    public int ReadyCount => _queue.Jobs.Count(j => j.Status == JobStatus.Pending);
    public int ProcessingCount => _queue.Jobs.Count(j => j.Status == JobStatus.Running);
    public int CompletedCount => _queue.Jobs.Count(j => j.Status == JobStatus.Completed);
    public int SkippedCount => _queue.Jobs.Count(j => j.Status == JobStatus.Skipped);
    public int FailedCount => _queue.Jobs.Count(j => j.Status == JobStatus.Failed);
    public int RetryableCount => FailedCount;
    public int RemainingCount => ReadyCount + ProcessingCount;

    public bool HasFiles => TotalCount > 0;

    /// <summary>The large drag-and-drop target only exists while the queue is empty.</summary>
    public bool ShowDropZone => TotalCount == 0;

    /// <summary>Once files exist the drop target shrinks to a hint so a long batch keeps its room.</summary>
    public bool ShowDropHint => TotalCount > 0;

    public string DropHint => "Drag audio files or folders here, or use Add files.";

    /// <summary>True when the batch can actually start, using the same state the rows show.</summary>
    public bool CanRun
    {
        get
        {
            if (IsRunning) return false;
            if (ReadyCount == 0) return false;
            if (SelectedStems == Stems.None) return false;
            if (string.IsNullOrWhiteSpace(OutputFolder) || !Directory.Exists(OutputFolder)) return false;

            if (!IsSimulatedEngine)
            {
                if (string.IsNullOrWhiteSpace(_settings.LiveExePath) || !File.Exists(_settings.LiveExePath))
                    return false;

                if (!(Version.TryParse(_settings.LiveVersion, out var version) && version >= new Version(12, 1)))
                    return false;
            }

            return true;
        }
    }

    /// <summary>"2 failed · 0 ready", "Ready to run", "3 of 8 done" - reserved for a batch that can start.</summary>
    public string BatchSummaryText
    {
        get
        {
            if (IsRunning)
                return $"{CompletedCount + SkippedCount + FailedCount} of {TotalCount} done";

            if (TotalCount == 0) return "No files queued";

            if (FailedCount > 0 && ReadyCount == 0) return $"{FailedCount} failed · 0 ready";
            if (CanRun) return "Ready to run";
            if (FailedCount > 0) return $"{ReadyCount} ready · {FailedCount} failed";
            if (ReadyCount > 0) return $"{ReadyCount} ready";
            if (CompletedCount > 0 && SkippedCount > 0) return $"{CompletedCount} completed · {SkippedCount} skipped";
            if (CompletedCount > 0) return $"{CompletedCount} completed";
            if (SkippedCount > 0) return $"{SkippedCount} skipped";

            return "No files queued";
        }
    }

    /// <summary>Colour key for the summary: running / failed / ready / neutral / idle.</summary>
    public string BatchSummaryLevel
    {
        get
        {
            if (IsRunning) return "running";
            if (FailedCount > 0) return "failed";
            if (CanRun) return "ready";
            if (TotalCount == 0) return "idle";
            if (ReadyCount > 0) return "ready-pending";
            return "neutral";
        }
    }

    /// <summary>Plain-language reason the batch cannot start. Empty when it can.</summary>
    public string BlockerText
    {
        get
        {
            if (IsRunning) return string.Empty;

            // The exact combined case the design calls for.
            if (ReadyCount == 0 && string.IsNullOrWhiteSpace(OutputFolder))
                return "Add a supported file and choose an output folder.";

            if (SelectedStems == Stems.None) return "Select at least one stem.";

            if (string.IsNullOrWhiteSpace(OutputFolder)) return "Choose an output folder.";
            if (!Directory.Exists(OutputFolder)) return "The output folder no longer exists. Choose it again.";

            if (!IsSimulatedEngine)
            {
                if (string.IsNullOrWhiteSpace(_settings.LiveExePath) || !File.Exists(_settings.LiveExePath))
                    return "Ableton Live was not found. Open Diagnostics to locate it.";

                if (!(Version.TryParse(_settings.LiveVersion, out var version) && version >= new Version(12, 1)))
                    return "Ableton Live 12.1 or newer is required.";
            }

            if (ReadyCount == 0)
            {
                if (FailedCount > 0) return "No files are ready. Retry the failed tracks, or add new files.";
                if (TotalCount > 0) return "Nothing left to do. Add more files, or clear completed.";
            }

            return string.Empty;
        }
    }

    public bool ShowBlocker => !IsRunning && !CanRun && !string.IsNullOrEmpty(BlockerText);

    public bool HasFailures => FailedCount > 0;

    public string FailurePillText => $"{FailedCount} failed";

    /// <summary>"Vocals · Drums · Bass · Others" for All, the chosen stems otherwise.</summary>
    public string StemsSummaryText => SelectedStems switch
    {
        Stems.All => "Vocals · Drums · Bass · Others",
        Stems.Drums => "Drums",
        Stems.None => "None selected",
        _ => string.Join(" · ", SelectedStems.Enumerate().Select(s => s.DisplayName())),
    };

    public bool ShowAlertIcon => !IsRunning && FailedCount > 0;

    public bool ShowCheckIcon => !IsRunning && FailedCount == 0 && CanRun;

    /// <summary>Per-track progress is only shown when the engine reports a reliable value.</summary>
    public bool ShowTrackPercent { get => _showTrackPercent; private set => Set(ref _showTrackPercent, value); }

    /// <summary>
    /// Only an active run can be "busy": at rest the bar stays an empty track rather than
    /// pulsing, and while a run is active but the engine reports no reliable percentage the
    /// bar shows the indeterminate busy state instead of inventing a number.
    /// </summary>
    public bool IsProgressIndeterminate => IsRunning && !ShowTrackPercent;

    public double TrackPercent { get => _trackPercent; private set => Set(ref _trackPercent, value); }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            OnPropertyChanged(nameof(ShowProgress));
            OnPropertyChanged(nameof(ShowBlocker));
            OnPropertyChanged(nameof(BatchSummaryText));
            OnPropertyChanged(nameof(BatchSummaryLevel));
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(ShowAlertIcon));
            OnPropertyChanged(nameof(ShowCheckIcon));
            RaiseCommandStates();
        }
    }

    /// <summary>The progress row only exists during a run.</summary>
    public bool ShowProgress => IsRunning;

    public bool StopAfterCurrent
    {
        get => _stopAfterCurrent;
        set
        {
            if (!Set(ref _stopAfterCurrent, value)) return;
            if (_runner is not null) _runner.StopAfterCurrent = value;
            if (value) StatusMessage = "Will stop after the current track finishes.";
            StopAfterCurrentCommand.RaiseCanExecuteChanged();
        }
    }

    // ------------------------------------------------------------------ stems

    private bool _isAllStems = true;
    private bool _isDrumsOnly;
    private bool _isCustomStems;
    private bool _stemVocals = true;
    private bool _stemDrums = true;
    private bool _stemBass = true;
    private bool _stemOthers = true;

    public bool IsAllStems
    {
        get => _isAllStems;
        set { if (Set(ref _isAllStems, value) && value) { SetAllStems(); } }
    }

    public bool IsDrumsOnly
    {
        get => _isDrumsOnly;
        set { if (Set(ref _isDrumsOnly, value) && value) { SetDrumsOnly(); } }
    }

    public bool IsCustomStems
    {
        get => _isCustomStems;
        set { if (Set(ref _isCustomStems, value) && value) { PresetChanged(StemsPreset.Custom); } }
    }

    public bool StemVocals { get => _stemVocals; set { if (Set(ref _stemVocals, value)) CustomStemToggled(); } }
    public bool StemDrums { get => _stemDrums; set { if (Set(ref _stemDrums, value)) CustomStemToggled(); } }
    public bool StemBass { get => _stemBass; set { if (Set(ref _stemBass, value)) CustomStemToggled(); } }
    public bool StemOthers { get => _stemOthers; set { if (Set(ref _stemOthers, value)) CustomStemToggled(); } }

    private void SetAllStems()
    {
        _stemVocals = _stemDrums = _stemBass = _stemOthers = true;
        RaiseStemFlags();
        PresetChanged(StemsPreset.All);
    }

    private void SetDrumsOnly()
    {
        _stemVocals = _stemBass = _stemOthers = false;
        _stemDrums = true;
        RaiseStemFlags();
        PresetChanged(StemsPreset.DrumsOnly);
    }

    private void CustomStemToggled()
    {
        _isCustomStems = true;
        _isAllStems = false;
        _isDrumsOnly = false;
        OnPropertyChanged(nameof(IsCustomStems));
        OnPropertyChanged(nameof(IsAllStems));
        OnPropertyChanged(nameof(IsDrumsOnly));
        RaiseStemFlags();
        PresetChanged(StemsPreset.Custom);
    }

    private void RaiseStemFlags()
    {
        OnPropertyChanged(nameof(StemVocals));
        OnPropertyChanged(nameof(StemDrums));
        OnPropertyChanged(nameof(StemBass));
        OnPropertyChanged(nameof(StemOthers));
        OnPropertyChanged(nameof(SelectedStems));
        OnPropertyChanged(nameof(StemsSummaryText));
    }

    private void PresetChanged(StemsPreset preset)
    {
        _settings.LastStemsPreset = preset;
        _settings.LastStems = SelectedStems;
        SaveSettings();
        StatusMessage = SelectedStems == Stems.None
            ? "Select at least one stem before running."
            : $"Stems: {SelectedStems.Describe()} ({SelectedStems.Count()} of 4).";
        RefreshRowState();
    }

    public Stems SelectedStems
    {
        get
        {
            var stems = Stems.None;
            if (_stemVocals) stems |= Stems.Vocals;
            if (_stemDrums) stems |= Stems.Drums;
            if (_stemBass) stems |= Stems.Bass;
            if (_stemOthers) stems |= Stems.Others;
            return stems;
        }
    }

    private void ApplyStemsFromSettings()
    {
        var stems = _settings.LastStems == Stems.None ? Stems.All : _settings.LastStems;
        _stemVocals = stems.HasFlag(Stems.Vocals);
        _stemDrums = stems.HasFlag(Stems.Drums);
        _stemBass = stems.HasFlag(Stems.Bass);
        _stemOthers = stems.HasFlag(Stems.Others);

        switch (_settings.LastStemsPreset)
        {
            case StemsPreset.DrumsOnly:
                _isDrumsOnly = true;
                break;
            case StemsPreset.Custom:
                _isCustomStems = true;
                break;
            default:
                _isAllStems = true;
                break;
        }
    }

    // ------------------------------------------------------------------ mode

    private bool _isHighSpeed = true;
    private bool _isHighQuality;

    public bool IsHighSpeed
    {
        get => _isHighSpeed;
        set
        {
            if (!Set(ref _isHighSpeed, value) || !value) return;
            _isHighQuality = false;
            OnPropertyChanged(nameof(IsHighQuality));
            PersistMode(ProcessingMode.HighSpeed);
        }
    }

    public bool IsHighQuality
    {
        get => _isHighQuality;
        set
        {
            if (!Set(ref _isHighQuality, value) || !value) return;
            _isHighSpeed = false;
            OnPropertyChanged(nameof(IsHighSpeed));
            PersistMode(ProcessingMode.HighQuality);
        }
    }

    private void PersistMode(ProcessingMode mode)
    {
        _settings.LastMode = mode;
        SaveSettings();
        StatusMessage = $"Processing mode: {mode.DisplayName()}.";
        RefreshRowState();
    }

    public ProcessingMode SelectedMode => _isHighQuality ? ProcessingMode.HighQuality : ProcessingMode.HighSpeed;

    public string StemModeHint =>
        "High Speed separates all selected stems in a single pass. High Quality uses separate passes " +
        "for vocals, drums and bass, which can take longer; excluding 'Others' shortens High Quality " +
        "because it no longer needs the remaining-pass. Both settings are frozen onto each job when " +
        "you press Run, so a Drums Only or High Quality result never satisfies an All Stems or High Speed job.";

    // ------------------------------------------------------------------ options

    private bool _isRecursiveImport;
    private bool _isKeepAwake = true;
    private bool _isSimulatedEngine;

    public bool IsRecursiveImport
    {
        get => _isRecursiveImport;
        set { if (Set(ref _isRecursiveImport, value)) { _settings.RecursiveImport = value; SaveSettings(); } }
    }

    public bool IsKeepAwake
    {
        get => _isKeepAwake;
        set { if (Set(ref _isKeepAwake, value)) { _settings.KeepAwakeDuringRun = value; SaveSettings(); } }
    }

    public bool IsSimulatedEngine
    {
        get => _isSimulatedEngine;
        set
        {
            if (!Set(ref _isSimulatedEngine, value)) return;
            _settings.UseSimulatedRunner = value;
            SaveSettings();
            UpdateLiveStatus();
            StatusMessage = value
                ? "Simulated engine enabled: results are NOT from Ableton Live."
                : "Ableton Live engine enabled.";
        }
    }

    // ------------------------------------------------------------------ queue ops

    public int AddFiles() => AddPaths(PickFiles());

    public int AddFolder() => AddPaths(PickFolder());

    /// <summary>Adds files/folders dropped onto the window.</summary>
    public int AddDropped(IEnumerable<string> paths) => AddPaths(paths);

    private int AddPaths(IEnumerable<string> paths)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0) return 0;

        var destination = DestinationRoot();
        var profile = new JobProfile(SelectedStems == Stems.None ? Stems.All : SelectedStems, SelectedMode);
        var added = _queue.AddPaths(list, IsRecursiveImport, profile, destination);

        RefreshJobs();
        StatusMessage = added == 0
            ? "Nothing new was added (already in the queue)."
            : $"Added {added} file(s).";
        RaiseCommandStates();
        _log.Info($"queue: added {added} item(s) from {list.Count} input(s)");
        return added;
    }

    /// <summary>Rows are built with a live view of the batch settings so drift can be shown.</summary>
    private JobRowViewModel CreateRow(Job job) =>
        new(job, () => new JobProfile(SelectedStems == Stems.None ? Stems.All : SelectedStems, SelectedMode));

    private void RemoveJob(JobRowViewModel? row)
    {
        if (row is null) return;
        if (row.Job.Status == JobStatus.Running)
        {
            MessageBox.Show("That track is currently running and cannot be removed.", "Skeer autoSTEM",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _queue.Remove(row.Id);
        Jobs.Remove(row);
        Recalculate();
        StatusMessage = $"Removed {row.FileName} from the queue.";
    }

    private void ToggleRowDetails(JobRowViewModel? row)
    {
        if (row is null) return;
        row.IsExpanded = !row.IsExpanded;
    }

    private void ClearCompleted()
    {
        var removed = _queue.ClearCompleted();
        RefreshJobs();
        StatusMessage = $"Cleared {removed} finished item(s) from the queue (files on disk were kept).";
    }

    private void RetryFailed()
    {
        var changed = _queue.RetryFailed();
        RefreshJobs();
        StatusMessage = changed == 0 ? "There are no failed tracks to retry." : $"{changed} track(s) put back in the queue.";
    }

    private void RefreshJobs()
    {
        Jobs.Clear();
        foreach (var job in _queue.Jobs) Jobs.Add(CreateRow(job));
        OnPropertyChanged(nameof(Jobs));
        Recalculate();
    }

    /// <summary>
    /// Re-reads per-row state. Used when the batch settings change, so a row saved with
    /// different settings starts saying so.
    /// </summary>
    private void RefreshRowState()
    {
        foreach (var row in Jobs) row.Refresh();
        Recalculate();
    }

    /// <summary>
    /// Recomputed from the queue after every change, so the command bar and the rows always
    /// describe the same state - including after a run whose own counters have been reset.
    /// </summary>
    private void Recalculate()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(ProcessingCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(SkippedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(RetryableCount));
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(ShowDropZone));
        OnPropertyChanged(nameof(ShowDropHint));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(BatchSummaryText));
        OnPropertyChanged(nameof(BatchSummaryLevel));
        OnPropertyChanged(nameof(BlockerText));
        OnPropertyChanged(nameof(ShowBlocker));
        OnPropertyChanged(nameof(HasFailures));
        OnPropertyChanged(nameof(FailurePillText));
        OnPropertyChanged(nameof(StemsSummaryText));
        OnPropertyChanged(nameof(ShowAlertIcon));
        OnPropertyChanged(nameof(ShowCheckIcon));
        RaiseCommandStates();
    }

    // ------------------------------------------------------------------ run

    public async Task RunAsync()
    {
        if (IsRunning) return;

        if (!SeparatorFactory.IsImplemented(_settings.Engine))
        {
            MessageBox.Show(
                $"The {_settings.Engine} engine is not in this build.\n\n" +
                "Switch back to the Ableton Live engine in Settings.",
                "Skeer autoSTEM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedStems == Stems.None)
        {
            MessageBox.Show("Select at least one stem before running.", "Skeer autoSTEM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputFolder) || !Directory.Exists(OutputFolder))
        {
            MessageBox.Show("Choose an output folder that exists before running.", "Skeer autoSTEM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LiveInstall? install = null;
        if (!IsSimulatedEngine)
        {
            install = ResolveLiveInstall();
            if (install is null) return;

            if (!install.SupportsStemSeparation)
            {
                MessageBox.Show(
                    $"The located Ableton Live build ({install.DisplayName}) does not have stem separation.\n\n" +
                    "Live 12.1 or newer is required.",
                    "Skeer autoSTEM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ShowRunConditions(install)) return;
            if (!ConfirmLiveTakeover()) return;
        }

        var template = install is null
            ? string.Empty
            : WorkingProjectBuilder.FindBaseTemplate(install) ?? string.Empty;
        if (install is not null && string.IsNullOrEmpty(template))
        {
            MessageBox.Show(
                "Live's audio-track template could not be found, so the working project cannot be built.\n\n" +
                "Please check the Ableton Live installation and try again.",
                "Skeer autoSTEM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Freeze the current settings onto every unfinished job.
        _queue.FreezeSettings(new JobProfile(SelectedStems, SelectedMode), DestinationRoot());
        RefreshJobs();

        var jobs = _queue.Jobs.ToList();
        if (jobs.All(j => j.Status is JobStatus.Completed or JobStatus.Skipped))
        {
            StatusMessage = "Everything in the queue is already done.";
            return;
        }

        using var guard = new LiveSessionGuard();
        if (!guard.Acquired)
        {
            MessageBox.Show(
                "Another Skeer autoSTEM run is already controlling Ableton Live.\n\n" +
                "Only one run may drive Live at a time.",
                "Skeer autoSTEM", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _runCancellation = new CancellationTokenSource();
        IsRunning = true;
        StopAfterCurrent = false;
        StatusLevel = "Running";
        ResetCounters();

        var engine = SeparatorFactory.CreateForRun(_settings.Engine, _log, IsSimulatedEngine);

        _runner = new BatchRunner(engine);

        var context = new BatchContext
        {
            Settings = _settings,
            Install = install ?? new LiveInstall(string.Empty, "0.0", "Simulated"),
            BaseTemplatePath = template,
            Log = _log,
            PersistQueue = () => _queue.Save(),
            Progress = new Progress<BatchProgress>(OnProgress),
        };

        _log.Info($"run requested by user: output '{OutputFolder}', engine {engine.Name}");

        try
        {
            var summary = await Task.Run(() => _runner.RunAsync(jobs, context, _runCancellation.Token));

            // Counts come from the queue, not from the summary, so they agree with the rows.
            Recalculate();

            var message = $"Run finished in {summary.Duration:hh\\:mm\\:ss}.\n\n" +
                          $"Completed: {summary.Completed}\nSkipped: {summary.Skipped}\nFailed: {summary.Failed}\n" +
                          $"Remaining: {summary.Remaining}\n\nLog: {summary.LogPath}";

            if (summary.Stopped && summary.StopReason != ErrorKind.None)
                message += "\n\nPaused: " + summary.StopMessage;

            StatusMessage = summary.Stopped ? "Run stopped. " + summary.StopMessage : "Run finished.";
            StatusLevel = summary.Stopped && summary.Failed > 0 ? "Error" : "Idle";

            // Reflect the finished state before the summary dialog blocks the UI thread,
            // so the command bar does not still claim a run is in progress behind it.
            _queue.Save();
            RefreshJobs();
            IsRunning = false;

            ShowResults(summary, message);
        }
        catch (Exception ex)
        {
            _log.Error($"run failed: {ex}");
            StatusMessage = "Run failed: " + ex.Message;
            StatusLevel = "Error";
            MessageBox.Show("The run failed:\n\n" + ex.Message, "Skeer autoSTEM",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _queue.Save();
            RefreshJobs();
            IsRunning = false;
            StopAfterCurrent = false;
            _runner = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    private void ShowResults(BatchSummary summary, string message)
    {
        var result = MessageBox.Show(message + "\n\nOpen the results folder now?",
            "Skeer autoSTEM - run finished", MessageBoxButton.YesNo,
            summary.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
        {
            var latest = Jobs.FirstOrDefault(r => r.CanOpenResults);
            OpenFolder(latest?.ResultPath ?? OutputFolder);
        }
        else
        {
            OpenLogFolderCommand.Execute(null);
        }
    }

    private void OnProgress(BatchProgress progress)
    {
        if (progress.TrackName is not null) CurrentTrack = progress.TrackName;
        CurrentStage = progress.Stage.DisplayName() + (string.IsNullOrEmpty(progress.Detail) ? string.Empty : $" - {progress.Detail}");

        if (progress.Percent is { } percent)
        {
            ShowTrackPercent = true;
            TrackPercent = percent;
        }
        else
        {
            ShowTrackPercent = false;
        }
        OnPropertyChanged(nameof(IsProgressIndeterminate));

        var row = Jobs.FirstOrDefault(j => j.Id == progress.JobId);
        row?.Refresh();

        // The queue is the source of truth for the counters shown in the command bar.
        Recalculate();
    }

    private void ResetCounters()
    {
        Recalculate();
        ShowTrackPercent = false;
        TrackPercent = 0;
        OnPropertyChanged(nameof(IsProgressIndeterminate));
    }

    // ------------------------------------------------------------------ live location

    private void UpdateLiveStatus()
    {
        if (IsSimulatedEngine)
        {
            LiveStatus = "Engine: simulated (no Ableton Live required).";
            OnPropertyChanged(nameof(LiveChip));
            OnPropertyChanged(nameof(LiveExePath));
            Recalculate();
            return;
        }

        var install = ResolveLiveInstall(save: true);
        LiveStatus = install is null
            ? "Ableton Live: not found. Use Locate Live or browse to the executable."
            : $"Ableton Live: {install.DisplayName}";
        OnPropertyChanged(nameof(LiveChip));
        OnPropertyChanged(nameof(LiveExePath));
        Recalculate();
    }

    private LiveInstall? ResolveLiveInstall(bool save = true)
    {
        LiveInstall? install = null;

        if (!string.IsNullOrWhiteSpace(_settings.LiveExePath))
            install = LiveLocator.DescribeExisting(_settings.LiveExePath);

        install ??= LiveLocator.Locate();

        if (install is not null && save &&
            (!PathSanitizer.SamePath(install.ExePath, _settings.LiveExePath ?? string.Empty) || _settings.LiveVersion != install.Version))
        {
            _settings.LiveExePath = install.ExePath;
            _settings.LiveVersion = install.Version;
            _settings.LiveEdition = install.Edition;
            SaveSettings();
            _log.Info($"verified Ableton Live: {install.DisplayName} at {install.ExePath}");
        }

        return install;
    }

    private void LocateLive()
    {
        var installs = LiveLocator.FindAll();
        if (installs.Count == 0)
        {
            MessageBox.Show(
                "No Ableton Live installation was found automatically.\n\n" +
                "Use 'Browse' to point Skeer autoSTEM at Ableton Live's executable.",
                "Skeer autoSTEM", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var chosen = installs.First();
        _settings.LiveExePath = chosen.ExePath;
        _settings.LiveVersion = chosen.Version;
        _settings.LiveEdition = chosen.Edition;
        SaveSettings();
        UpdateLiveStatus();
        StatusMessage = $"Located {chosen.DisplayName}.";
    }

    private void BrowseForLive()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select the Ableton Live executable",
            Filter = "Ableton Live (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        var install = LiveLocator.DescribeExisting(dialog.FileName);
        if (install is null)
        {
            MessageBox.Show("That file could not be read as an Ableton Live executable.", "Skeer autoSTEM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.LiveExePath = install.ExePath;
        _settings.LiveVersion = install.Version;
        _settings.LiveEdition = install.Edition;
        SaveSettings();
        UpdateLiveStatus();
        StatusMessage = $"Using {install.DisplayName}.";
    }

    // ------------------------------------------------------------------ dialogs & shell

    private IEnumerable<string> PickFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add audio files",
            Multiselect = true,
            Filter = $"Audio files|{BuildFilter()}|All files (*.*)|*.*",
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    private IEnumerable<string> PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = IsRecursiveImport
                ? "Add audio files from a folder (including subfolders)"
                : "Add audio files from a folder (top level only)",
        };

        return dialog.ShowDialog() == true ? new[] { dialog.FolderName } : Array.Empty<string>();
    }

    private static string BuildFilter() =>
        string.Join(";", AudioFileTypes.SupportedExtensions.Select(e => "*" + e));

    private void ChooseOutputFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the output folder" };
        if (Directory.Exists(OutputFolder)) dialog.InitialDirectory = OutputFolder;
        if (dialog.ShowDialog() != true) return;

        OutputFolder = dialog.FolderName;
        StatusMessage = "Output folder: " + OutputFolder;
    }

    /// <summary>Only completed, verified output opens as a result.</summary>
    private void OpenResults(JobRowViewModel? row)
    {
        if (row is null || !row.CanOpenResults) return;
        OpenFolder(row.ResultPath);
    }

    /// <summary>Partial output is only ever offered when it was actually verified.</summary>
    private void OpenPartial(JobRowViewModel? row)
    {
        if (row is null || !row.CanOpenPartial) return;
        OpenFolder(row.PartialPath);
    }

    private void OpenFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;

        try
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open '{folder}':\n{ex.Message}", "Skeer autoSTEM",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// A run closes every Ableton Live session it finds, so the user gets one look at what that
    /// means while the window titles still say. Only asked when something is actually open.
    /// </summary>
    private bool ConfirmLiveTakeover()
    {
        var open = LiveSessionTakeover.Detect();
        if (open.Count == 0) return true;

        var list = string.Join("\n", open.Select(session => $"  - {session.Description}"));

        var text =
            "A run needs Ableton Live to itself, so Live is closed and reopened for each " +
            $"track.\n\nThese sessions are open now and will be saved and closed:\n\n{list}\n\n" +
            "A set that was already saved is written where it lives. A set that was never saved " +
            $"goes to\n\n  {AppPaths.RecoveryRoot}\n\n" +
            "under a name built from its title and the clock, so nothing of yours is replaced. A " +
            "save that cannot be completed automatically is dropped rather than left blocking the " +
            "run.\n\nClose these sessions and start the batch?";

        var answer = MessageBox.Show(text, "Skeer autoSTEM - Ableton Live is already open",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);

        _log.Info(answer == MessageBoxResult.OK
            ? $"user confirmed takeover of {open.Count} open Live session(s)"
            : "run not started: the open Live sessions were left alone");

        return answer == MessageBoxResult.OK;
    }

    private bool ShowRunConditions(LiveInstall install)
    {
        if (_settings.HasConfirmedRunConditions) return true;

        var text =
            "Before the batch starts, please leave these conditions in place:\n\n" +
            $"  - Ableton Live: {install.DisplayName}\n" +
            "  - Keep the desktop unlocked. Windows lock or UAC prompts stop automation safely.\n" +
            "  - A run owns Ableton Live and closes it between tracks. Anything still open is\n" +
            "    saved first - unsaved sets go to the recovered folder - and you are asked\n" +
            "    before a session is closed, so save any work you care about up front.\n" +
            "  - Do not use the mouse/keyboard over the Live window while a track is processing.\n" +
            "  - Windows will be asked to stay awake for the duration of the run.\n" +
            "  - A display scale factor other than 100% is handled, but the Live window must\n" +
            "    stay on the monitor where it was launched (it is maximised there).\n\n" +
            "Start the batch now?";

        var answer = MessageBox.Show(text, "Skeer autoSTEM - overnight run conditions",
            MessageBoxButton.OKCancel, MessageBoxImage.Information);

        if (answer != MessageBoxResult.OK) return false;

        _settings.HasConfirmedRunConditions = true;
        SaveSettings();
        return true;
    }

    private void SaveSettings()
    {
        _settings.LastStems = SelectedStems;
        _settingsStore.Save(_settings);
    }

    private string DestinationRoot() => string.IsNullOrWhiteSpace(OutputFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Skeer autoSTEM")
        : OutputFolder;

    private void RaiseCommandStates()
    {
        RunCommand?.RaiseCanExecuteChanged();
        StopAfterCurrentCommand?.RaiseCanExecuteChanged();
        RetryFailedCommand?.RaiseCanExecuteChanged();
        OpenOutputFolderCommand?.RaiseCanExecuteChanged();
        ClearCompletedCommand?.RaiseCanExecuteChanged();
    }

    private void LogLine(string message) => _log.Info(message);

    public void Shutdown()
    {
        try
        {
            _runCancellation?.Cancel();
            _queue.Save();
            _log.Dispose();
        }
        catch
        {
            // shutting down; nothing useful to do
        }
    }
}

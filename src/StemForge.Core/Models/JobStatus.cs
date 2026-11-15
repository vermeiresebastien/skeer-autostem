namespace StemForge.Core.Models;

public enum JobStatus
{
    /// <summary>Waiting to run (also the state an interrupted job returns to).</summary>
    Pending,

    /// <summary>Currently being processed.</summary>
    Running,

    /// <summary>All requested stems produced, verified and published.</summary>
    Completed,

    /// <summary>No work needed: an identical, already-verified result exists.</summary>
    Skipped,

    /// <summary>Stopped with an actionable error.</summary>
    Failed,
}

/// <summary>
/// Machine-readable failure classification so the UI can show an actionable message
/// and the batch runner knows whether it is safe to continue.
/// </summary>
public enum ErrorKind
{
    None = 0,

    // Source problems (safe to skip and continue).
    SourceMissing,
    SourceChanged,
    UnsupportedFormat,
    SourceUnreadable,

    // Destination problems (fatal for the whole batch until fixed).
    DestinationUnwritable,
    DestinationMissing,
    DiskFull,

    // Live environment problems (pause the batch, safe to resume once fixed).
    LiveNotInstalled,
    LiveVersionUnsupported,
    /// <summary>
    /// No longer raised: a run now saves and closes any Live it finds instead of pausing
    /// (see LiveSessionTakeover). The member stays because the queue persists these values
    /// positionally and old rows still name it.
    /// </summary>
    LiveAlreadyRunningWithUserProject,
    LiveStartupFailed,
    LiveWindowNotFound,
    LiveLostFocus,
    DesktopLocked,
    LiveCrashed,
    UnexpectedDialog,
    LiveErrorDialog,
    SeparationTimeout,
    SeparationCancelled,
    StemsMissing,
    StemsUnverified,
    PublishFailed,

    // Coordination.
    AnotherRunnerActive,
    Cancelled,
    Unknown,

    /// <summary>
    /// Live has the Separate Stems command in its menu but will not enable it, which means the
    /// clip is not selected in the way the command needs. Added last because the queue persists
    /// these values positionally and older rows still name the earlier ones.
    /// </summary>
    ClipNotSelected,
}

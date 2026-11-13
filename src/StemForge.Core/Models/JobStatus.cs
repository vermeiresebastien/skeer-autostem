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
}

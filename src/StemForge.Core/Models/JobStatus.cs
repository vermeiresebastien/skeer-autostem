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
///
/// The groups below are about the cause. Whether a failure pauses the batch or walks on is
/// <c>BatchRunner.ShouldPauseBatch</c>'s list, and this ordering is not that rule. One outcome
/// never reaches the list: a user stop is returned as "continue" from the cancellation path in
/// <c>ProcessJobAsync</c>, so <see cref="Cancelled"/> is not in it. A queue file names these
/// values (<see cref="StemForge.Core.Support.JsonStore"/> serialises enums as strings), so an old
/// row loads whatever order this enum is in; what an old row needs is that the name still exists.
/// </summary>
public enum ErrorKind
{
    None = 0,

    // Source problems: this job fails, the batch goes on to the next one.
    SourceMissing,
    SourceChanged,
    UnsupportedFormat,
    SourceUnreadable,

    // Destination problems: the batch stops, and the queue is preserved until they are fixed.
    DestinationUnwritable,
    DestinationMissing,
    DiskFull,

    // Live environment and engine problems: the batch pauses and the queue stays intact for a
    // resume once the cause is fixed. Which failures do that is ShouldPauseBatch's list, not this
    // grouping - the groups here are about what the cause is.
    LiveNotInstalled,
    LiveVersionUnsupported,
    /// <summary>
    /// No longer raised: a run now saves and closes any Live it finds instead of pausing
    /// (see LiveSessionTakeover). The member stays because a queue file names failures and an old
    /// row still names this one; a name this build does not have makes the whole file unreadable
    /// (both measured by QueuePersistenceTests, not assumed here).
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

    // Costed this job alone: ShouldPauseBatch walks the batch on past every one of these.
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
    /// clip is not selected in the way the command needs. Where a member sits in this list is not
    /// load-bearing: a queue file names these values, which
    /// QueuePersistenceTests.AFailureCauseIsWrittenByNameRatherThanByPosition measures, and nothing
    /// reads the numbers. What is load-bearing is the name itself -
    /// EngineSeamTests.EveryFailureCauseAQueueCanNameStillExists holds the set this build must keep
    /// answering, which is why the retired member above is still declared.
    /// </summary>
    ClipNotSelected,
}

using StemForge.Core.Models;

namespace StemForge.Core.Engines;

/// <summary>
/// Failures are reported against the engine that raised them. "Ableton Live lost the
/// foreground" is a true and useful sentence about the Live engine and a nonsense sentence
/// about an engine that never opens a window, so it cannot be allowed to reach the queue that
/// way - a batch paused on an impossible cause is a batch nobody can fix.
/// </summary>
public static class EngineFailures
{
    /// <summary>Causes that only an engine which drives Ableton Live can honestly report.</summary>
    private static readonly ErrorKind[] LiveOnly =
    [
        ErrorKind.LiveNotInstalled,
        ErrorKind.LiveVersionUnsupported,
        ErrorKind.LiveAlreadyRunningWithUserProject,
        ErrorKind.LiveStartupFailed,
        ErrorKind.LiveWindowNotFound,
        ErrorKind.LiveLostFocus,
        ErrorKind.LiveCrashed,
        ErrorKind.UnexpectedDialog,
        ErrorKind.LiveErrorDialog,
        ErrorKind.ClipNotSelected,
        ErrorKind.DesktopLocked,
        ErrorKind.AnotherRunnerActive,
    ];

    public static bool IsLiveOnly(ErrorKind kind) => Array.IndexOf(LiveOnly, kind) >= 0;

    /// <summary>
    /// The kind a failure is recorded as for this engine. A Live-only kind from a non-Live
    /// engine becomes <see cref="ErrorKind.Unknown"/>: the message still says what happened,
    /// but the batch is not paused for a reason that cannot apply to it.
    /// </summary>
    public static ErrorKind Normalize(EngineKind engine, ErrorKind kind) =>
        engine != EngineKind.Live && IsLiveOnly(kind) ? ErrorKind.Unknown : kind;
}

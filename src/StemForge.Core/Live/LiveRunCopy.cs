using System.Collections.Generic;

namespace StemForge.Core.Live;

/// <summary>
/// What the app says to the user before a run touches Ableton Live, in one place.
///
/// These sentences carry a promise about other people's work - "a set that cannot be saved is
/// left open, the run stops before anything is closed" - and a promise that lives inside the body
/// of a method calling <c>MessageBox.Show</c> cannot be read by anything except a human, because
/// nobody can reach the string without showing the dialog. Moved here, the same words the user
/// sees are data a test can hold, which is the only way a check can fail when a later change
/// turns "left open" back into "dropped".
///
/// The view model supplies the machine-specific parts (which Live, which folder, which sessions
/// are open) and composes nothing, so there is no second copy of these sentences to drift.
/// </summary>
public static class LiveRunCopy
{
    /// <summary>
    /// The confirmation shown while the window titles still say what is open, before a run saves
    /// and closes every Live it finds.
    /// </summary>
    /// <param name="recoveryRoot">
    /// Passed in rather than read here so a check can name the folder it expects; the app calls
    /// the one-argument form and gets <see cref="StemForge.Core.Support.AppPaths.RecoveryRoot"/>.
    /// </param>
    public static string TakeoverConfirmation(
        IReadOnlyList<RunningLiveSession> open, string? recoveryRoot = null)
    {
        var list = Sessions(open);
        var folder = recoveryRoot ?? StemForge.Core.Support.AppPaths.RecoveryRoot;

        return
            "A run needs Ableton Live to itself, so Live is closed and reopened for each " +
            $"track.\n\nThese sessions are open now and will be saved and closed:\n\n{list}\n\n" +
            "A set that was already saved is written where it lives. A set that was never saved " +
            $"goes to\n\n  {folder}\n\n" +
            "under a name built from its title and the clock, so nothing of yours is replaced. A " +
            "set that cannot be saved is not thrown away either: it is left open, the run stops " +
            "before anything is closed, and the log names the session it stopped on. Save it in " +
            "Live, then start the batch again.\n\n" +
            "Close these sessions and start the batch?";
    }

    /// <summary>What the log records when the user accepts the takeover.</summary>
    public static string TakeoverConfirmed(int sessionCount) =>
        $"user confirmed takeover of {sessionCount} open Live session(s)";

    /// <summary>
    /// What the log records when the user declines. This is the sentence that makes the refusal
    /// auditable after the fact: the sessions were left alone, not touched and not saved.
    /// </summary>
    public static string TakeoverDeclined() =>
        "run not started: the open Live sessions were left alone";

    /// <summary>
    /// The conditions a run needs left in place, asked once. The takeover bullet is the same
    /// promise as <see cref="TakeoverConfirmation"/>, in the form the user reads before anything
    /// is open.
    /// </summary>
    public static string RunConditions(string liveDisplayName) =>
        "Before the batch starts, please leave these conditions in place:\n\n" +
        $"  - Ableton Live: {liveDisplayName}\n" +
        $"    Stem separation needs {LiveInstall.RequirementPhrase}. The app refuses a build it can\n" +
        "    identify as Standard, Intro or Lite - and identification is by the name the install\n" +
        "    carries, not by a licence check. Where nothing names an edition, the app cannot tell\n" +
        "    and does not refuse: Live's own menu is the last word there.\n" +
        "  - A run that takes over Ableton Live never discards an open set. If a session has\n" +
        "    unsaved changes that cannot be named and written to the recovery folder, the run\n" +
        "    stops before closing anything and names the session it left open; only a session\n" +
        "    proven saved, or one with no unsaved changes, is ever closed or terminated.\n" +
        "  - Each track takes the screen for a few seconds: Live only accepts its own menu\n" +
        "    and clip commands while it is the window in front, so it is raised, clicked,\n" +
        "    and the screen is handed back to you before the render starts.\n" +
        "  - The render itself needs nothing. Use the machine, or walk away and lock the\n" +
        "    screen - a lock stops the next track's clicks, and nothing is ever typed into\n" +
        "    a lock screen. Resume the batch after unlocking.\n" +
        "  - Do not click inside the Live window during those few seconds per track.\n" +
        "  - Windows will be asked to stay awake for the duration of the run.\n" +
        "  - A display scale factor other than 100% is handled, but the Live window must\n" +
        "    stay on the monitor where it was launched (it is maximised there).\n\n" +
        "Start the batch now?";

    private static string Sessions(IReadOnlyList<RunningLiveSession> open)
    {
        var lines = new List<string>(open.Count);
        foreach (var session in open) lines.Add($"  - {session.Description}");
        return string.Join("\n", lines);
    }
}

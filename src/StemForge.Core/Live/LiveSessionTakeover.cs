using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using StemForge.Core.Live.Automation;
using StemForge.Core.Logging;
using StemForge.Core.Models;
using StemForge.Core.Support;

namespace StemForge.Core.Live;

/// <summary>An Ableton Live instance that is running right now.</summary>
public sealed record RunningLiveSession(int ProcessId, string Title, LiveSessionKind Kind)
{
    /// <summary>How the session is described to the user before anything is closed.</summary>
    public string Description => string.IsNullOrWhiteSpace(Title)
        ? $"a Live window that is still opening (pid {ProcessId})"
        : $"\"{Title}\" (pid {ProcessId})";
}

/// <summary>
/// Thrown when a Live session must not be closed because its unsaved work could not be saved.
/// The message is written for the user: it names the session and says nothing was closed.
/// </summary>
/// <remarks>
/// <see cref="LiveSessionTakeover.CloseAllAsync"/> throws this instead of returning a count, so a
/// caller that is about to launch its own Live cannot ignore the refusal and start a second
/// instance on top of somebody's open set. The run's generic failure path pauses the batch and
/// shows the message, which is the behaviour a refusal needs.
/// </remarks>
public sealed class LiveTakeoverRefusedException : Exception
{
    public LiveTakeoverRefusedException(string sessionTitle, string message) : base(message) =>
        SessionTitle = sessionTitle;

    /// <summary>The window title of the session that was left open.</summary>
    public string SessionTitle { get; }
}

/// <summary>What a save attempt established about the session's set.</summary>
public enum SaveState
{
    /// <summary>Live's title carried no unsaved marker: there was nothing to write.</summary>
    NoUnsavedChanges,

    /// <summary>The set is on disk: either a confirmed Save As wrote our recovery file, or Live cleared its own marker.</summary>
    Saved,

    /// <summary>Unsaved changes exist and nothing proved they reached the disk.</summary>
    Failed,
}

/// <summary>
/// How an attempt to name Live's save dialog ended. One value per greppable log reason, so the
/// next real occurrence says which step failed instead of "the dialog could not be named".
/// </summary>
public enum SaveDialogOutcome
{
    /// <summary>The path was written and the dialog went away.</summary>
    Ok,

    /// <summary>No edit control the file name could be written into was found.</summary>
    NoEditControl,

    /// <summary>The field exists but refused the value, and typing it would need the user's screen.</summary>
    ValueNotSet,

    /// <summary>No usable confirm button (none matched, or the one that did was disabled) and the dialog refused a posted Enter.</summary>
    NoSaveButton,

    /// <summary>Enter was posted and the dialog ignored it.</summary>
    EnterIgnored,

    /// <summary>The confirm button was pressed but the dialog stayed up.</summary>
    DialogStillOpen,
}

/// <summary>One naming attempt: the outcome plus what the automation tree actually offered.</summary>
public readonly record struct SaveNamingAttempt(SaveDialogOutcome Outcome, string Evidence)
{
    /// <summary>The greppable reason code for this outcome, e.g. <c>value-not-set</c>.</summary>
    public string Reason => Outcome switch
    {
        SaveDialogOutcome.Ok => "ok",
        SaveDialogOutcome.NoEditControl => "no-edit-control",
        SaveDialogOutcome.ValueNotSet => "value-not-set",
        SaveDialogOutcome.NoSaveButton => "no-save-button",
        SaveDialogOutcome.EnterIgnored => "enter-ignored",
        SaveDialogOutcome.DialogStillOpen => "dialog-still-open",
        _ => "unknown",
    };
}

/// <summary>Whether a session may be closed, and whether it may be killed once it refuses to go.</summary>
public enum SessionClose
{
    /// <summary>The process was already gone.</summary>
    AlreadyGone,

    /// <summary>The session is gone and its work is safe.</summary>
    Closed,

    /// <summary>The session holds unsaved work that could not be saved; it was left open.</summary>
    Refused,
}

/// <summary>The result of trying to take over one session.</summary>
public sealed record SessionCloseResult(SessionClose Outcome, SaveState Save, string Title, string Message)
{
    public bool Closed => Outcome is SessionClose.Closed or SessionClose.AlreadyGone;
}

/// <summary>The bounded waits a takeover runs on.</summary>
public sealed record TakeoverTiming(
    TimeSpan WindowGrace,
    TimeSpan SaveDialogGrace,
    TimeSpan SaveCompleteGrace,
    TimeSpan CloseBudget,
    int MaxPrompts)
{
    public static TakeoverTiming Default { get; } = new(
        WindowGrace: TimeSpan.FromSeconds(5),
        SaveDialogGrace: TimeSpan.FromSeconds(3),
        SaveCompleteGrace: TimeSpan.FromSeconds(5),
        CloseBudget: TimeSpan.FromSeconds(25),
        MaxPrompts: 3);
}

/// <summary>
/// The process-level actions a takeover performs. Every one of them either sends input or has a
/// visible effect, which is why they are a seam: the rules above them can then be exercised
/// against a window that is not somebody's Live, without a real click, keystroke or kill.
/// </summary>
public interface ITakeoverActions
{
    /// <summary>Brings the session to the foreground, which is what a Ctrl+S needs.</summary>
    bool TryActivateMain(IntPtr main);

    /// <summary>
    /// Asks <paramref name="main"/> - and only that window - to save, and reports whether the
    /// keystroke went out. A refusal is an answer, not a crash: it means the session was not in a
    /// state where a save could be trusted to land, which is exactly what the unsaved-work policy
    /// needs to know before it lets anything close.
    /// </summary>
    InjectionResult RequestSave(IntPtr main);

    /// <summary>Asks the window to close the way its title-bar X would.</summary>
    void RequestClose(IntPtr main);

    /// <summary>Whether the session process is still running.</summary>
    bool IsSessionAlive(int processId);

    /// <summary>Ends the process and everything it started.</summary>
    void Terminate(int processId);
}

/// <summary>The real actions: the ones that touch Live, the desktop and the process table.</summary>
public sealed class LiveTakeoverActions : ITakeoverActions
{
    public static LiveTakeoverActions Instance { get; } = new();

    public bool TryActivateMain(IntPtr main) => WindowControl.TryActivate(main);

    /// <summary>
    /// Ctrl+S, aimed at the session that was just brought forward and guarded on the way out.
    ///
    /// It cannot be posted: a posted key message carries no modifier state, and the window
    /// procedure that decides whether to save reads the real Ctrl key. So this is the one place a
    /// takeover still injects - which is why it asks the guard who is actually in front rather than
    /// trusting the activation a few milliseconds earlier. A keystroke meant for a save dialog is not
    /// a keystroke to send blind: it lands wherever the user is typing.
    /// </summary>
    public InjectionResult RequestSave(IntPtr main) =>
        GuardedInput.PressChord(main, InputSimulator.VkControl, InputSimulator.VkS, "asked Live to save");

    public void RequestClose(IntPtr main) => CloseRequest.CloseWindow(main);

    public bool IsSessionAlive(int processId) => LiveProcessWindows.IsAlive(processId);

    public void Terminate(int processId)
    {
        using var proc = Process.GetProcessById(processId);
        proc.Kill(entireProcessTree: true);
        proc.WaitForExit(5000);
    }
}

/// <summary>
/// The rule that keeps a takeover from costing somebody their afternoon: a session with unsaved
/// work is only ever closed once that work is on disk, and only such a session may be killed.
///
/// It lives in one place, with one name, because the alternative - the rule being implied by the
/// order of two statements in a sixty-line method - is how a later change quietly turns "save
/// first" back into "close anyway".
/// </summary>
public static class UnsavedWorkPolicy
{
    /// <summary>The name that appears in every log line this policy decides.</summary>
    public const string Name = "unsaved-work-policy";

    /// <summary>
    /// A close request may only be sent when the set is saved or had no unsaved changes. A failed
    /// save is not retried away: the session stays open so the work is still there.
    /// </summary>
    public static bool MayClose(SaveState save) => save != SaveState.Failed;

    /// <summary>
    /// The bounded kill survives only for a session that is provably saved or has no unsaved
    /// changes. Killing a Live that is holding unsaved work is data loss with a stack trace.
    /// </summary>
    public static bool MayTerminate(SaveState save) =>
        save is SaveState.Saved or SaveState.NoUnsavedChanges;

    /// <summary>What the user is told when a session was left open, and what to do next.</summary>
    public static string Refusal(string sessionDescription, string reason) =>
        $"Live session {sessionDescription} has unsaved changes that could not be saved ({reason}). " +
        "The run stopped and nothing was closed in that session; save it in Live, then start the run again.";

    /// <summary>The one line that justifies a kill, naming the policy that allowed it.</summary>
    public static string TerminateJustification(int processId, SaveState save) =>
        $"Live (pid {processId}) did not close on its own; terminating it under {Name}, which allows " +
        "a bounded kill only for a set that is provably saved or has no unsaved changes " +
        $"({Describe(save)})";

    /// <summary>The reason no kill is allowed, in the same words the log uses.</summary>
    public static string TerminateRefusal(int processId) =>
        $"Live (pid {processId}) did not close on its own and will not be terminated: {Name} never " +
        "kills a session holding unsaved work";

    public static string Describe(SaveState save) => save switch
    {
        SaveState.Saved => "saved",
        SaveState.NoUnsavedChanges => "no unsaved changes",
        _ => "save failed",
    };
}

/// <summary>
/// Closes every Ableton Live instance so the batch can drive its own.
///
/// Live is single-instance and there is no documented way to hand a set to one that is already
/// running, so a run has to own the process outright. The contract with the user is therefore
/// "a run takes over Live", not "a run refuses to start while Live is open" - and taking over
/// means saving what was open first, because the alternative is losing somebody's afternoon.
///
/// Five rules keep this from becoming the thing that hangs or the thing that loses work:
///   * unsaved work is never discarded - a set that cannot be named and saved leaves the session
///     open and stops the run (<see cref="UnsavedWorkPolicy"/>);
///   * a file dialog is a save opportunity, never an obstacle: it is named and confirmed, and it
///     is not charged to the prompt budget that message boxes are;
///   * every wait is bounded, and the last resort is a process kill - but only for a session whose
///     set is provably saved or had nothing unsaved;
///   * a button is only pressed when its label matches exactly, because a substring match on
///     "No" also matches "Monitor" and the wrong click here is worse than no click;
///   * nothing is ever overwritten: an unsaved set goes to the recovery folder under a name
///     that did not exist a moment earlier.
/// </summary>
public static class LiveSessionTakeover
{
    private const string NativeDialogClass = "#32770";

    /// <summary>Labels that abandon unsaved changes, matched exactly.</summary>
    private static readonly string[] DiscardLabels =
    {
        "Don't Save", "Don’t Save", "Dont Save", "Do Not Save", "Close without saving", "No",
    };

    /// <summary>Labels that confirm a file dialog, matched exactly.</summary>
    private static readonly string[] ConfirmLabels =
    {
        "Save", "OK", "Open",
    };

    private static readonly TimeSpan ConfirmGrace = TimeSpan.FromSeconds(3);

    public static IReadOnlyList<RunningLiveSession> Detect()
    {
        var launchedByUs = LiveLaunchRecord.ReadCurrent();
        var sessions = new List<RunningLiveSession>();

        foreach (var proc in LiveProcessWindows.FindLiveProcesses())
        {
            using (proc)
            {
                var title = WindowControl.GetMainWindowTitle(proc);
                var kind = LiveSessionClassifier.Classify(title, launchedByUs?.ProcessId == proc.Id);
                sessions.Add(new RunningLiveSession(proc.Id, title, kind));
            }
        }

        return sessions;
    }

    /// <summary>
    /// Saves and closes every running Live. Returns how many are gone.
    ///
    /// A session that cannot be reached is logged and skipped, because the caller is about to
    /// launch its own Live and a stale instance is a visible problem, not a silent one. A session
    /// whose unsaved work cannot be saved is different: it is not skipped, because the caller must
    /// not go on to launch a second Live on top of it, so this ends with
    /// <see cref="LiveTakeoverRefusedException"/> and the run stops with an actionable message.
    /// </summary>
    public static async Task<int> CloseAllAsync(RunLog log, Job? job = null, CancellationToken ct = default)
    {
        var running = LiveProcessWindows.FindLiveProcesses();
        if (running.Count == 0) return 0;

        var launchedByUs = LiveLaunchRecord.ReadCurrent();
        var closed = 0;
        var refused = new List<SessionCloseResult>();

        foreach (var proc in running)
        {
            using (proc)
            {
                try
                {
                    var result = await CloseSessionAsync(
                        proc, launchedByUs?.ProcessId == proc.Id, log, job, ct).ConfigureAwait(false);

                    if (result.Outcome == SessionClose.Closed) closed++;
                    else if (result.Outcome == SessionClose.Refused) refused.Add(result);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.Warn($"could not take over Live (pid {proc.Id}): {ex.Message}", job);
                }
            }
        }

        if (refused.Count > 0)
        {
            // The launch record is kept: it still attributes the process that is running.
            var first = refused[0];
            throw new LiveTakeoverRefusedException(first.Title, first.Message);
        }

        LiveLaunchRecord.Clear();
        log.Info($"took over {closed} of {running.Count} Ableton Live session(s)", job);
        return closed;
    }

    /// <summary>
    /// Saves and closes one Live session that the caller already knows about. Used when the
    /// runner is putting its own working session away, which raises the same prompts as taking
    /// somebody else's over. A refusal is logged and reported as "not closed"; this never throws,
    /// because it is called from a finally block that must not replace the run's own failure.
    /// </summary>
    public static async Task<bool> CloseAsync(Process proc, RunLog log, Job? job = null, CancellationToken ct = default)
    {
        var launchedByUs = LiveLaunchRecord.ReadCurrent()?.ProcessId == proc.Id;
        var result = await CloseSessionAsync(proc, launchedByUs, log, job, ct).ConfigureAwait(false);

        if (result.Outcome == SessionClose.Refused) log.Error(result.Message, job);
        return result.Closed;
    }

    /// <summary>
    /// Takes over one session: save if there is anything to save, close it, answer what it puts up
    /// on the way out, and terminate only what the policy allows. The outcome says which of those
    /// happened, so the caller never has to infer it from a boolean.
    /// </summary>
    public static async Task<SessionCloseResult> CloseSessionAsync(
        Process proc,
        bool launchedByUs,
        RunLog log,
        Job? job,
        CancellationToken ct,
        ITakeoverActions? actions = null,
        TakeoverTiming? timing = null)
    {
        actions ??= LiveTakeoverActions.Instance;
        timing ??= TakeoverTiming.Default;

        if (!actions.IsSessionAlive(proc.Id))
            return new SessionCloseResult(SessionClose.AlreadyGone, SaveState.NoUnsavedChanges, string.Empty,
                "the process had already exited");

        var title = WindowControl.GetMainWindowTitle(proc);
        var kind = LiveSessionClassifier.Classify(title, launchedByUs);
        var session = new RunningLiveSession(proc.Id, title, kind);
        log.Info($"closing Live session: {session.Description}", job);

        var main = await WaitForWindowAsync(proc, timing, actions, ct).ConfigureAwait(false);

        // Process.MainWindowTitle can come back empty for a window that is right there - it is a
        // cached answer, and a window parked off screen or one being redrawn reports nothing. The
        // window itself still knows its name, and the name is what a refusal has to show the user,
        // so once the handle is found the handle wins.
        if (main != IntPtr.Zero)
        {
            var liveTitle = WindowControl.GetTitle(main);
            if (!string.IsNullOrWhiteSpace(liveTitle) && !string.Equals(liveTitle, title, StringComparison.Ordinal))
            {
                title = liveTitle;
                kind = LiveSessionClassifier.Classify(title, launchedByUs);
                session = new RunningLiveSession(proc.Id, title, kind);
                log.Info($"Live session title read from its window: {session.Description}", job);
            }
        }

        SaveState save;
        string why;

        if (main != IntPtr.Zero)
        {
            (save, why) = await SaveOpenSetAsync(proc, main, log, job, actions, timing, ct).ConfigureAwait(false);
        }
        else if (launchedByUs)
        {
            // A window-less process this app started itself cannot be holding the user's work.
            save = SaveState.NoUnsavedChanges;
            why = "it is a session this app launched and it has no window of its own";
            log.Info($"Live (pid {proc.Id}) has no window and was launched by this app; it holds no user work", job);
        }
        else if (!HasUnsavedChanges(title))
        {
            save = SaveState.NoUnsavedChanges;
            why = "Live's title carried no unsaved marker";
        }
        else
        {
            save = SaveState.Failed;
            why = "the session had no window to save it from";
        }

        if (!UnsavedWorkPolicy.MayClose(save))
        {
            var message = UnsavedWorkPolicy.Refusal(session.Description, why);
            log.Error(message, job);
            return new SessionCloseResult(SessionClose.Refused, save, title, message);
        }

        if (main != IntPtr.Zero)
        {
            actions.RequestClose(main);
        }
        else
        {
            log.Warn($"Live (pid {proc.Id}) has no window to close; it is waited on and then terminated", job);
        }

        var wait = await AnswerPromptsAsync(proc, main, title, log, job, save, actions, timing, ct)
            .ConfigureAwait(false);

        if (wait.Exited)
            return new SessionCloseResult(SessionClose.Closed, wait.Save, title, "closed");

        if (wait.Refusal is { } refusal)
        {
            var message = UnsavedWorkPolicy.Refusal(session.Description, refusal);
            log.Error(message, job);
            return new SessionCloseResult(SessionClose.Refused, SaveState.Failed, title, message);
        }

        if (!UnsavedWorkPolicy.MayTerminate(wait.Save))
        {
            var message = UnsavedWorkPolicy.TerminateRefusal(proc.Id);
            log.Error(message, job);
            return new SessionCloseResult(SessionClose.Refused, wait.Save, title, message);
        }

        log.Warn(UnsavedWorkPolicy.TerminateJustification(proc.Id, wait.Save), job);

        try
        {
            actions.Terminate(proc.Id);
        }
        catch (Exception ex)
        {
            log.Warn($"Live (pid {proc.Id}) refused to be terminated: {ex.Message}", job);
        }

        return actions.IsSessionAlive(proc.Id)
            ? new SessionCloseResult(SessionClose.Refused, wait.Save, title,
                $"Live (pid {proc.Id}) did not close and could not be terminated.")
            : new SessionCloseResult(SessionClose.Closed, wait.Save, title, "terminated");
    }

    /// <summary>
    /// The window to save into, if Live shows one. One glance is not enough: Live blanks its title
    /// bar while a render runs, and the session closed a second after a job failed is the one still
    /// holding that modal. Terminating a Live that only needed a moment to show its window is what
    /// put the "Live unexpectedly quit" question in front of the next launch.
    /// </summary>
    private static async Task<IntPtr> WaitForWindowAsync(
        Process proc, TakeoverTiming timing, ITakeoverActions actions, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timing.WindowGrace;

        while (DateTime.UtcNow < deadline)
        {
            if (!actions.IsSessionAlive(proc.Id)) return IntPtr.Zero;

            var main = LiveProcessWindows.MainWindow(proc.Id)?.Handle ?? IntPtr.Zero;
            if (main != IntPtr.Zero) return main;
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------
    // Saving: proving the work is on disk before anything closes
    // ------------------------------------------------------------------

    /// <summary>
    /// Saves the open set and says what was proven. Ctrl+S into the focused session: a named set
    /// is written where it lives and no dialog appears; a set that was never saved opens the file
    /// dialog, which gets a recovery name rather than a default one, because the default is
    /// "Untitled.als" in whoever's documents folder and, worse, may overwrite the last such set.
    ///
    /// The proof matters more than the attempt. A dialog counts as saved only when the recovery
    /// file is actually on disk - a dialog that was answered by cancelling it, or that the user
    /// closed, looks exactly like a successful save from the outside. A set with no dialog counts
    /// as saved only when Live cleared its own unsaved marker.
    /// </summary>
    private static async Task<(SaveState State, string Why)> SaveOpenSetAsync(
        Process proc,
        IntPtr main,
        RunLog log,
        Job? job,
        ITakeoverActions actions,
        TakeoverTiming timing,
        CancellationToken ct)
    {
        var title = WindowControl.GetTitle(main);

        if (!HasUnsavedChanges(title))
            return (SaveState.NoUnsavedChanges, "Live's title carried no unsaved marker");

        if (!actions.TryActivateMain(main))
            return (SaveState.Failed, "the session would not take the foreground, so a save could not be asked for");

        try
        {
            // The keystroke is refused unless this session is the window actually in front, and a
            // refusal is reported the same way a failed save is: nothing that cannot be proven saved
            // gets closed.
            var save = actions.RequestSave(main);
            if (!save.Injected)
                return (SaveState.Failed, $"the save keystroke was refused ({save.Reason}): {save.Detail}");
        }
        catch (Exception ex)
        {
            return (SaveState.Failed, $"the save keystroke was refused: {ex.Message}");
        }

        var dialog = await WaitForSaveDialogAsync(proc, main, timing, ct).ConfigureAwait(false);

        if (dialog is not null)
        {
            var path = RecoveryPath(title, AppPaths.RecoveryRoot, DateTimeOffset.Now);
            var attempt = TryNameFileDialog(dialog, path);

            log.Info($"save dialog of Live (pid {proc.Id}): reason={attempt.Reason} path=\"{path}\" " +
                     $"found={attempt.Evidence}", job);

            if (attempt.Outcome != SaveDialogOutcome.Ok)
                return (SaveState.Failed, $"the save dialog could not be named (reason={attempt.Reason})");

            if (!await WaitForFileAsync(path, timing.SaveCompleteGrace, ct).ConfigureAwait(false))
                return (SaveState.Failed, $"the save dialog was answered but no file appeared at \"{path}\"");

            log.Info($"wrote the unsaved set to {path}", job);
            return (SaveState.Saved, $"saved to \"{path}\"");
        }

        if (await WaitForMarkerClearedAsync(main, timing.SaveCompleteGrace, ct).ConfigureAwait(false))
        {
            log.Info($"Live (pid {proc.Id}) saved \"{WindowControl.GetTitle(main)}\" in place", job);
            return (SaveState.Saved, "saved in place");
        }

        return (SaveState.Failed, "Ctrl+S raised no dialog and Live still marks the set unsaved");
    }

    /// <summary>
    /// Waits for the save dialog that Ctrl+S may raise, or returns null when none does. Only a
    /// dialog that blocks the session is considered: Live's own windows that do not - the browser,
    /// preferences - are not a save opportunity, and treating one as a dialog would either refuse
    /// a takeover or type into a window that has nothing to do with saving.
    /// </summary>
    private static async Task<TopLevelWindow?> WaitForSaveDialogAsync(
        Process proc, IntPtr main, TakeoverTiming timing, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timing.SaveDialogGrace;

        while (DateTime.UtcNow < deadline)
        {
            foreach (var window in LiveProcessWindows.OtherVisibleWindows(proc.Id, main))
            {
                if (!BlocksSession(main, window)) continue;
                if (IsFileSaveDialog(window)) return window;
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Waits for Live to drop its own unsaved marker, which is its "written" signal.</summary>
    private static async Task<bool> WaitForMarkerClearedAsync(IntPtr main, TimeSpan budget, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + budget;

        while (DateTime.UtcNow < deadline)
        {
            if (!HasUnsavedChanges(WindowControl.GetTitle(main))) return true;
            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        return !HasUnsavedChanges(WindowControl.GetTitle(main));
    }

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan budget, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + budget;

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        return File.Exists(path);
    }

    /// <summary>
    /// Writes the recovery path into a file dialog and confirms it. Both steps have to work, and
    /// the dialog has to actually go away, or nothing is claimed - and the dialog is never
    /// dismissed with a cancel label, because cancelling it drops the changes it was asking about.
    ///
    /// Neither step needs the screen: the value goes in through ValuePattern, and the confirmation
    /// is a pattern Invoke or a posted key. A dialog that refuses the value is refused in turn -
    /// typing it would mean the user's own window has to receive keystrokes.
    /// </summary>
    public static SaveNamingAttempt TryNameFileDialog(TopLevelWindow dialog, string path, TimeSpan? confirmGrace = null)
    {
        var root = LiveUi.FromHandle(dialog.Handle);
        if (root is null)
            return new SaveNamingAttempt(SaveDialogOutcome.NoEditControl, "no automation root");

        var edit = LiveUi.FindFileNameEdit(root);
        var buttons = CollectButtons(root);
        var evidence = $"edit={(edit is null ? "<none>" : "\"" + LiveUi.Name(edit) + "\"")} " +
                       $"buttons=[{string.Join("|", buttons.Select(b => b.Name))}]";

        if (edit is null) return new SaveNamingAttempt(SaveDialogOutcome.NoEditControl, evidence);

        if (!LiveUi.TrySetValueWithoutTyping(edit, path))
            return new SaveNamingAttempt(SaveDialogOutcome.ValueNotSet, evidence);

        var grace = confirmGrace ?? ConfirmGrace;
        var confirm = Pick(buttons, ConfirmLabels);

        // Only an enabled button is a button. A disabled one answers an Invoke pattern without
        // complaining and without doing anything, which would be recorded as "pressed it and the
        // dialog stayed" when nothing was actually pressable.
        DialogButton? pressable = null;
        if (confirm is { } candidate && LiveUi.IsEnabled(candidate.Element)) pressable = candidate;

        // A pattern Invoke is the dialog's own Save button; it is preferred because the button is
        // named and a named button cannot be the wrong one.
        var invoked = pressable is { } pressed && LiveUi.TryInvokePattern(pressed.Element);
        if (invoked && WaitForDialogClosed(dialog, grace))
            return new SaveNamingAttempt(SaveDialogOutcome.Ok, evidence);

        // Enter is the dialog's own default action, posted into its queue. The window is not
        // activated and nothing is clicked: a save must not need the user's screen.
        var posted = InputSimulator.PostKey(dialog.Handle, InputSimulator.VkReturn);
        if (posted && WaitForDialogClosed(dialog, grace))
            return new SaveNamingAttempt(SaveDialogOutcome.Ok, evidence);

        // "The button is there but will not take a press" is a different fault from "the dialog
        // has no button we recognise and ignores Enter", and the difference is what the next log
        // line has to say.
        if (invoked) return new SaveNamingAttempt(SaveDialogOutcome.DialogStillOpen, evidence);
        if (confirm is not null) return new SaveNamingAttempt(SaveDialogOutcome.NoSaveButton, evidence);
        if (posted) return new SaveNamingAttempt(SaveDialogOutcome.EnterIgnored, evidence);
        return new SaveNamingAttempt(SaveDialogOutcome.NoSaveButton, evidence);
    }

    /// <summary>
    /// Watches the session out the door, answering whatever it puts up on the way.
    ///
    /// A file dialog here is a save opportunity and is named, not answered - and it does not spend
    /// the prompt budget, because that budget exists to bound message boxes, and a bound on saves
    /// would be a bound on keeping the work.
    ///
    /// A message box is answered by <see cref="Press"/>, and that is what the call below with
    /// <see cref="DiscardLabels"/> does, in this order: it takes the dialog's buttons and looks for
    /// one whose full label equals a discard label; it posts that button's own first-letter
    /// accelerator into this dialog's message queue, but only when no other button starts with the
    /// same letter (<see cref="Automation.DialogKey.ForLabel"/>), and waits two seconds for the
    /// dialog to go; if that did not take, it raises the dialog and presses the button with a real
    /// click, which is guarded and re-reads the foreground and the point it lands on
    /// (<see cref="LiveUi.TryClick"/>), because Live's message boxes ignore a programmatic Invoke
    /// and that silent non-answer is what used to end in a killed process. If no label matched, or
    /// neither answer took, it posts Escape instead - which cancels the prompt and leaves the
    /// dialog for the next poll rather than pressing a button it is not certain of.
    ///
    /// A prompt is answered only while the title still says the set is clean. The verdict that
    /// licenses the discard was proven before the close began, so when a prompt is up the marker
    /// is read again first: a set that went dirty after entry is work made after the run started,
    /// and the answer would throw it away on a judgment made before it existed. That prompt is
    /// left up unanswered and the close is refused, the same outcome any save that cannot be
    /// proven gets. What an answer was is logged - the label that took it, "Escape", or nothing -
    /// because "an answer happened" is not auditable against the label list.
    ///
    /// What it deliberately never does is press Save, and the reason is not that the answer would
    /// lose work: it cannot, because this loop is only entered after
    /// <see cref="UnsavedWorkPolicy.MayClose"/> has accepted the save state, so a session reaching
    /// this line is one whose set is proven on disk or had nothing unsaved in the first place.
    /// Clicking Save again would only open another dialog to negotiate with. A prompt that is
    /// still up when the close budget runs out is not answered by force either: the session's fate
    /// then goes to <see cref="UnsavedWorkPolicy.MayTerminate"/>, which allows the bounded kill
    /// for a set proven saved or one with nothing unsaved, and refuses the run for anything else.
    /// </summary>
    private static async Task<(bool Exited, string? Refusal, SaveState Save)> AnswerPromptsAsync(
        Process proc,
        IntPtr main,
        string sessionTitle,
        RunLog log,
        Job? job,
        SaveState save,
        ITakeoverActions actions,
        TakeoverTiming timing,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timing.CloseBudget;
        var answered = 0;
        var spentReported = false;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!actions.IsSessionAlive(proc.Id)) return (true, null, save);

            if (main != IntPtr.Zero)
            {
                var dialog = LiveProcessWindows.OtherVisibleWindows(proc.Id, main)
                    .FirstOrDefault(window => BlocksSession(main, window));

                if (dialog is not null && IsFileSaveDialog(dialog))
                {
                    var title = WindowControl.GetTitle(main);
                    if (string.IsNullOrWhiteSpace(title)) title = sessionTitle;

                    var path = RecoveryPath(title, AppPaths.RecoveryRoot, DateTimeOffset.Now);
                    var attempt = TryNameFileDialog(dialog, path);

                    log.Info($"save dialog while closing Live (pid {proc.Id}): reason={attempt.Reason} " +
                             $"path=\"{path}\" found={attempt.Evidence}", job);

                    if (attempt.Outcome != SaveDialogOutcome.Ok)
                    {
                        return (false,
                            $"a save dialog appeared while closing and could not be named (reason={attempt.Reason})",
                            SaveState.Failed);
                    }

                    if (!await WaitForFileAsync(path, timing.SaveCompleteGrace, ct).ConfigureAwait(false))
                    {
                        return (false,
                            $"the save dialog that appeared while closing was answered but no file appeared at \"{path}\"",
                            SaveState.Failed);
                    }

                    save = SaveState.Saved;
                    log.Info($"Live (pid {proc.Id}) asked to save on the way out; wrote {path} instead of " +
                             $"answering it, which is a save and not a prompt ({timing.MaxPrompts}-prompt budget untouched)", job);
                }
                else if (dialog is not null)
                {
                    // The verdict that licenses the discard below was proven before the close
                    // began, so when a prompt is up the marker is read again first: a set that
                    // went dirty after entry is work made after the run started, and answering
                    // the prompt with a discard would throw it away on a judgment made before it
                    // existed. The prompt is left up, unanswered, and the close is refused - the
                    // same outcome any save that cannot be proven gets.
                    if (HasUnsavedChanges(WindowControl.GetTitle(main)))
                    {
                        return (false,
                            "the set went unsaved while the close was running, so the prompt was " +
                            "not answered with a discard",
                            SaveState.Failed);
                    }

                    if (answered < timing.MaxPrompts)
                    {
                        answered++;
                        var answer = Press(dialog, DiscardLabels, fallBackToEscape: true);
                        log.Warn($"Live (pid {proc.Id}) put up '{dialog.Title}' while closing; " +
                                 $"answered with {(answer is null ? "nothing" : $"\"{answer}\"")} " +
                                 $"({answered} of {timing.MaxPrompts})", job);
                    }
                    else if (!spentReported)
                    {
                        spentReported = true;
                        log.Warn($"Live (pid {proc.Id}) is still asking after {timing.MaxPrompts} answers; " +
                                 "leaving further questions unanswered", job);
                    }
                }
            }

            await Task.Delay(300, ct).ConfigureAwait(false);
        }

        return (!actions.IsSessionAlive(proc.Id), null, save);
    }

    // ------------------------------------------------------------------
    // Dialogs: which window is which
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether a Live window is asking something, i.e. whether the session is blocked by it. Two
    /// signals, either sufficient: a native dialog class (Live's own prompts and file dialogs are
    /// common dialogs), or a main window that Windows has disabled because a modal is up. A window
    /// that is neither is one of Live's own panels, and answering it would press a key in a window
    /// nobody asked about.
    ///
    /// "Disabled" is read straight from the window, not through the automation tree: this is
    /// polled a few times a second for the whole close budget, and a UIA call per poll costs more
    /// than the answer is worth. A window that cannot be read counts as blocked, because guessing
    /// "not modal" is what lets a prompt go unanswered until the process is killed.
    /// </summary>
    public static bool BlocksSession(IntPtr main, TopLevelWindow window)
    {
        if (string.Equals(window.ClassName, NativeDialogClass, StringComparison.OrdinalIgnoreCase)) return true;
        if (main == IntPtr.Zero) return true;

        try
        {
            return !IsWindowEnabled(main);
        }
        catch
        {
            return true;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    /// <summary>
    /// Whether a window is a file dialog rather than a message box: it offers a field a file name
    /// can be written into. This is the distinction that decides "name and confirm" from "answer".
    /// </summary>
    public static bool IsFileSaveDialog(TopLevelWindow window)
    {
        var root = LiveUi.FromHandle(window.Handle);
        return root is not null && LiveUi.FindAnyEdit(root) is not null;
    }

    /// <summary>Whether the window is still on screen. A closed dialog stops being visible.</summary>
    private static bool WaitForDialogClosed(TopLevelWindow dialog, TimeSpan grace)
    {
        var deadline = DateTime.UtcNow + grace;

        while (DateTime.UtcNow < deadline)
        {
            if (!WindowControl.IsVisible(dialog.Handle)) return true;
            Thread.Sleep(150);
        }

        return !WindowControl.IsVisible(dialog.Handle);
    }

    /// <summary>
    /// The window's answer to a message box: the key that reaches the wanted button, then a real
    /// click on it, then Escape. In that order because the first costs the user nothing - a posted
    /// key goes into this dialog's queue, needs neither the foreground nor the screen, and cannot
    /// land in his window the way a click on a modal that never came forward can.
    ///
    /// Returns what the answer was: the label of the button that took it, "Escape" when only the
    /// fallback fired, or null when nothing took. The caller logs it, because "an answer happened"
    /// is not auditable against the label list.
    /// </summary>
    private static string? Press(TopLevelWindow dialog, string[] labels, bool fallBackToEscape)
    {
        var root = LiveUi.FromHandle(dialog.Handle);
        IReadOnlyList<DialogButton> buttons =
            root is null ? Array.Empty<DialogButton>() : CollectButtons(root);

        if (Pick(buttons, labels) is { } target)
        {
            if (DialogKey.ForLabel(target.Name, buttons.Select(b => b.Name)) is { } key
                && InputSimulator.PostKey(dialog.Handle, key)
                && WaitForDialogClosed(dialog, TimeSpan.FromSeconds(2))) return target.Name;

            // A raised real click, not Invoke: Live's own message boxes answer a click and ignore a
            // programmatic Invoke without reporting anything wrong. That silent "success" is what
            // left the save question unanswered for the whole close budget, after which the process
            // was killed and the crash flag set for the next launch. The click itself re-checks the
            // foreground and the point it lands on, so a dialog that went behind again between these
            // two lines is not answered in the user's window.
            if (WindowControl.TryActivateAndReport(dialog.Handle, attempts: 2).TargetIsForeground
                && LiveUi.TryClick(target.Element))
                return target.Name;
        }

        if (!fallBackToEscape) return null;

        try
        {
            WindowControl.Activate(dialog.Handle);
            GuardedInput.PressKey(dialog.Handle, InputSimulator.VkEscape, "escaped a Live prompt");
            return "Escape";
        }
        catch
        {
            // Nothing else to do; the caller's deadline and the policy cover it.
            return null;
        }
    }

    /// <summary>
    /// Interactive elements whose name is exactly one of the wanted labels, in the order the
    /// caller prefers them. Deliberately not a substring search: dialog button labels are short
    /// and "No" is a substring of "Monitor", "Analog" and "Not now".
    ///
    /// The preference order is the caller's, not the tree's. A Live message box exposes its own
    /// title-bar "Close" before the "No" that answers the question, so walking the tree and
    /// taking the first match would press the window's close box and leave the question asked.
    /// </summary>
    public static AutomationElement? FindButton(AutomationElement root, string[] labels) =>
        Pick(CollectButtons(root), labels)?.Element;

    /// <summary>Every button and menu item the dialog names, in tree order.</summary>
    public static IReadOnlyList<DialogButton> CollectButtons(AutomationElement root)
    {
        var candidates = new List<DialogButton>();

        foreach (var element in LiveUi.Descendants(root))
        {
            try
            {
                var type = element.Current.ControlType;
                if (type != ControlType.Button && type != ControlType.MenuItem) continue;

                var name = LiveUi.Name(element).Trim();
                if (name.Length > 0) candidates.Add(new DialogButton(name, element));
            }
            catch
            {
                // An element that cannot be read is not a button worth pressing.
            }
        }

        return candidates;
    }

    /// <summary>The wanted button, in the caller's preference order, or null when none is offered.</summary>
    public static DialogButton? Pick(IReadOnlyList<DialogButton> buttons, string[] labels)
    {
        foreach (var label in labels)
            foreach (var button in buttons)
                if (string.Equals(label, button.Name, StringComparison.OrdinalIgnoreCase))
                    return button;

        return null;
    }

    // ------------------------------------------------------------------
    // Unsaved state
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether a Live window title says the set has changes that are not on disk. Live marks them
    /// with a trailing asterisk after the set name - "Untitled*", "My Song*" - so the marker is
    /// read before the " - Ableton" suffix, not at the end of the whole title.
    ///
    /// A title that cannot be read (blank, or blanked while Live renders) counts as unsaved: the
    /// point of the marker is to prove the work is safe, and "we could not tell" is not proof.
    /// </summary>
    public static bool HasUnsavedChanges(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return true;

        var name = windowTitle.Trim();
        var separator = name.IndexOf(" - Ableton", StringComparison.OrdinalIgnoreCase);
        if (separator > 0) name = name[..separator];

        return name.TrimEnd().EndsWith('*');
    }

    // ------------------------------------------------------------------
    // Recovery naming
    // ------------------------------------------------------------------

    /// <summary>
    /// A file name for a set that has never been saved: what the window said it was, stamped with
    /// the time, in the recovery folder. The stamp is what makes a takeover safe to repeat, so the
    /// project part is the one that gets shortened, never the part that keeps names apart.
    /// </summary>
    public static string RecoveryName(string windowTitle, DateTimeOffset now)
    {
        const int stemBudget = 40;
        var stem = ProjectNameFromTitle(windowTitle);

        if (stem.Length > stemBudget) stem = stem[..stemBudget].TrimEnd('-', '.', ' ');
        if (stem.Length == 0) stem = "Live Set";

        return $"{stem}-{now:yyyyMMdd-HHmmss}";
    }

    /// <summary>
    /// Full path for a recovery save. The suffix keeps climbing while the name is taken, so a
    /// takeover cannot replace a file it did not create.
    /// </summary>
    public static string RecoveryPath(string windowTitle, string folder, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch
        {
            // A dialog pointed at a folder that cannot be created will be reported by the caller.
        }

        var baseName = RecoveryName(windowTitle, now);
        var candidate = baseName;
        var suffix = 2;

        while (File.Exists(Path.Combine(folder, candidate + ".als")) ||
               Directory.Exists(Path.Combine(folder, candidate + ".als")) ||
               Directory.Exists(Path.Combine(folder, candidate)))
        {
            candidate = $"{baseName}-{suffix++}";
        }

        return Path.Combine(folder, candidate + ".als");
    }

    /// <summary>
    /// The project part of a Live window title: "My Song.als - Ableton Live 12 Suite" is
    /// "My Song", "Untitled*" is "Untitled". The asterisk is Live's dirty marker, not a character
    /// that belongs in a file name.
    /// </summary>
    public static string ProjectNameFromTitle(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return "Live Set";

        var name = windowTitle.Trim();

        var separator = name.IndexOf(" - Ableton", StringComparison.OrdinalIgnoreCase);
        if (separator > 0) name = name[..separator];

        name = name.TrimEnd('*', ' ');

        var extension = Math.Max(name.LastIndexOf('.'), name.LastIndexOf(".als", StringComparison.OrdinalIgnoreCase));
        if (extension > 0) name = name[..extension];

        var cleaned = new string(name
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? ' ' : ch)
            .ToArray());

        while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
        cleaned = cleaned.Trim(' ', '.', '-');

        return cleaned.Length == 0 ? "Live Set" : cleaned;
    }
}

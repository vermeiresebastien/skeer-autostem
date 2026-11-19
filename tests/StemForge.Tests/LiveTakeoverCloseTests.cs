using System.Diagnostics;
using StemForge.Core.Live;
using StemForge.Core.Live.Automation;
using StemForge.Core.Logging;
using StemForge.Core.Support;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The close path end to end, against a session this test makes for itself: a titled main window,
/// the dialogs it raises and a recorded set of process actions. The takeover reads windows and
/// asks for actions, so the rules that decide whether somebody's set survives can be exercised
/// without an Ableton, a click, a keystroke or a kill.
///
/// Each case also asserts that no input was injected: the point of the fix is that a close is a
/// save first, and a save must not need the user's screen.
/// </summary>
public sealed class LiveTakeoverCloseTests
{
    private static TakeoverTiming Timing(int closeBudgetMs = 1500, int graceMs = 1500, int maxPrompts = 3) =>
        new(TimeSpan.FromMilliseconds(graceMs),
            TimeSpan.FromMilliseconds(graceMs),
            TimeSpan.FromMilliseconds(graceMs),
            TimeSpan.FromMilliseconds(closeBudgetMs),
            maxPrompts);

    /// <summary>Asserts the outcome and, when it is wrong, shows what the takeover said and logged.
    /// <paramref name="extra"/> carries evidence the takeover cannot see - the fake dialog's
    /// recorded write exception - so a refused save names its cause instead of just its absence.</summary>
    private static void AssertOutcome(SessionClose expected, SessionCloseResult result, Harness harness,
        string? extra = null) =>
        Assert.True(result.Outcome == expected,
            $"expected {expected}, got {result.Outcome} (save={result.Save}): {result.Message}" +
            (string.IsNullOrEmpty(extra) ? "" : $"\n{extra}") + $"\n--- log ---\n{harness.LogText}");

    /// <summary>One fake session plus the log it wrote and the actions it was asked for.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly List<string> _lines = new();

        internal Harness(string title)
        {
            Workspace = new TempWorkspace();
            Session = new FakeLiveSession(title);
            Proc = Process.GetCurrentProcess();
            Log = new RunLog(Workspace.Path("takeover.log"));
            Log.EntryWritten += (_, entry) =>
            {
                lock (_lines) _lines.Add(entry.Message);
            };
        }

        internal TempWorkspace Workspace { get; }

        internal FakeLiveSession Session { get; }

        internal FakeTakeoverActions Actions { get; } = new();

        internal RunLog Log { get; }

        internal Process Proc { get; }

        internal string LogText
        {
            get
            {
                lock (_lines) return string.Join("\n", _lines);
            }
        }

        internal Task<SessionCloseResult> CloseAsync(TakeoverTiming timing, bool launchedByUs = false) =>
            LiveSessionTakeover.CloseSessionAsync(Proc, launchedByUs, Log, null, CancellationToken.None, Actions, timing);

        public void Dispose()
        {
            Log.Dispose();
            Session.Dispose();
            Proc.Dispose();
            Workspace.Dispose();
        }
    }

    [Fact]
    public async Task AFileDialogRaisedByTheSaveRequestIsNamedAndTheSessionCloses()
    {
        var before = InputSimulator.InjectionAttempts;
        using var harness = new Harness("TakeoverFixture Save* - Ableton Live 12 Suite");

        FakeSaveDialog? dialog = null;
        harness.Actions.OnSave = () =>
        {
            dialog = harness.Session.RaiseDialog(() => new FakeSaveDialog(FakeSaveDialog.Confirm.WritesTheFile));
            dialog.FormClosed += (_, _) =>
            {
                harness.Session.CloseMain();
                harness.Actions.Alive = false;
            };
        };

        var result = await harness.CloseAsync(Timing());

        AssertOutcome(SessionClose.Closed, result, harness,
            dialog?.WriteError is null ? null : $"fake dialog write error:\n{dialog.WriteError}");
        Assert.Equal(SaveState.Saved, result.Save);
        Assert.Equal("closed", result.Message);

        Assert.NotNull(dialog);
        Assert.NotNull(dialog!.WrittenPath);
        Assert.True(File.Exists(dialog.WrittenPath),
            $"no file at \"{dialog.WrittenPath}\" (write error: {dialog.WriteError ?? "<none recorded>"} )");
        Assert.StartsWith(AppPaths.RecoveryRoot, dialog.WrittenPath);
        Assert.False(dialog.CancelPressed, "a save dialog must never be cancelled");
        Assert.False(dialog.EscapePressed);

        Assert.Contains("reason=ok", harness.LogText);
        Assert.Equal(1, harness.Actions.Saves);
        Assert.Equal(0, harness.Actions.Terminations);
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    [Fact]
    public async Task ASessionWhoseSaveDialogCannotBeNamedIsLeftOpenAndTheRunIsRefused()
    {
        var before = InputSimulator.InjectionAttempts;
        using var harness = new Harness("TakeoverFixture ReadOnly* - Ableton Live 12 Suite");

        FakeSaveDialog? dialog = null;
        harness.Actions.OnSave = () => dialog = harness.Session.RaiseDialog(
            () => new FakeSaveDialog(FakeSaveDialog.Confirm.WritesTheFile, readOnlyField: true));

        var result = await harness.CloseAsync(Timing());

        AssertOutcome(SessionClose.Refused, result, harness);
        Assert.Equal(SaveState.Failed, result.Save);
        Assert.False(result.Closed);

        // D1: the message names the session and says the work and the window are still there.
        Assert.Contains("TakeoverFixture ReadOnly", result.Message);
        Assert.Contains("nothing was closed", result.Message);

        Assert.Equal(0, harness.Actions.Closes);
        Assert.Equal(0, harness.Actions.Terminations);
        Assert.True(WindowControl.IsVisible(harness.Session.MainHandle), "the session must be left open");

        Assert.NotNull(dialog);
        Assert.Null(dialog!.WrittenPath);
        Assert.False(dialog.CancelPressed);
        Assert.Contains("reason=value-not-set", harness.LogText);
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    [Fact]
    public async Task ASessionThatWillNotTakeTheForegroundIsNeverClosedOrTerminated()
    {
        var before = InputSimulator.InjectionAttempts;
        using var harness = new Harness("TakeoverFixture Locked* - Ableton Live 12 Suite");
        harness.Actions.Activate = _ => false;

        var result = await harness.CloseAsync(Timing());

        AssertOutcome(SessionClose.Refused, result, harness);
        Assert.Equal(SaveState.Failed, result.Save);
        Assert.Contains("foreground", result.Message);
        Assert.Contains("nothing was closed", result.Message);

        Assert.Equal(0, harness.Actions.Saves);
        Assert.Equal(0, harness.Actions.Closes);
        Assert.Equal(0, harness.Actions.Terminations);
        Assert.True(WindowControl.IsVisible(harness.Session.MainHandle));
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    [Fact]
    public async Task ACleanSessionThatWillNotCloseIsTerminatedUnderThePolicy()
    {
        using var harness = new Harness("TakeoverFixture Clean - Ableton Live 12 Suite");

        var result = await harness.CloseAsync(Timing(closeBudgetMs: 700));

        AssertOutcome(SessionClose.Closed, result, harness);
        Assert.Equal(SaveState.NoUnsavedChanges, result.Save);
        Assert.Equal("terminated", result.Message);

        Assert.Equal(0, harness.Actions.Saves);       // nothing was unsaved, so nothing was asked for
        Assert.Equal(1, harness.Actions.Closes);
        Assert.Equal(1, harness.Actions.Terminations);

        // D4: the kill is allowed here, and the log says which named policy allowed it.
        Assert.Contains("unsaved-work-policy", harness.LogText);
        Assert.Contains("no unsaved changes", harness.LogText);
    }

    [Fact]
    public async Task AFileDialogRaisedWhileClosingIsNamedNotAnswered()
    {
        var before = InputSimulator.InjectionAttempts;
        using var harness = new Harness("TakeoverFixture Closing - Ableton Live 12 Suite");

        FakeSaveDialog? dialog = null;
        harness.Actions.OnClose = () =>
        {
            dialog = harness.Session.RaiseDialog(() => new FakeSaveDialog(FakeSaveDialog.Confirm.WritesTheFile));
            dialog.FormClosed += (_, _) =>
            {
                harness.Session.CloseMain();
                harness.Actions.Alive = false;
            };
        };

        // D2: a budget of zero message-box answers. The file dialog must still be named, because a
        // save is not a prompt.
        var result = await harness.CloseAsync(Timing(closeBudgetMs: 3000, maxPrompts: 0));

        AssertOutcome(SessionClose.Closed, result, harness,
            dialog?.WriteError is null ? null : $"fake dialog write error:\n{dialog.WriteError}");
        Assert.Equal(SaveState.Saved, result.Save);

        Assert.NotNull(dialog);
        Assert.NotNull(dialog!.WrittenPath);
        Assert.StartsWith(AppPaths.RecoveryRoot, dialog.WrittenPath);
        Assert.True(File.Exists(dialog.WrittenPath),
            $"no file at \"{dialog.WrittenPath}\" (write error: {dialog.WriteError ?? "<none recorded>"} )");
        Assert.False(dialog.CancelPressed);

        Assert.Contains("reason=ok", harness.LogText);
        Assert.Contains("save dialog while closing Live", harness.LogText);
        Assert.Contains("0-prompt budget untouched", harness.LogText);
        Assert.Equal(0, harness.Actions.Terminations);
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    [Fact]
    public async Task AWindowThatDoesNotBlockTheSessionIsNotTreatedAsASaveDialog()
    {
        var before = InputSimulator.InjectionAttempts;
        using var harness = new Harness("TakeoverFixture Panel* - Ableton Live 12 Suite");

        FakeSaveDialog? dialog = null;
        harness.Actions.OnSave = () => dialog = harness.Session.RaiseDialog(
            () => new FakeSaveDialog(FakeSaveDialog.Confirm.WritesTheFile), blockOwner: false);

        var result = await harness.CloseAsync(Timing(closeBudgetMs: 700, graceMs: 400));

        AssertOutcome(SessionClose.Refused, result, harness);
        Assert.Equal(SaveState.Failed, result.Save);
        Assert.Contains("Ctrl+S raised no dialog", result.Message);

        // Live's browser and preferences are windows too; naming one would type into a panel that
        // has nothing to do with saving.
        Assert.NotNull(dialog);
        Assert.Null(dialog!.WrittenPath);
        Assert.False(dialog.CancelPressed);
        Assert.False(dialog.EscapePressed);
        Assert.DoesNotContain("reason=", harness.LogText);

        Assert.Equal(0, harness.Actions.Closes);
        Assert.Equal(0, harness.Actions.Terminations);
        Assert.True(WindowControl.IsVisible(harness.Session.MainHandle));
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    /// <summary>
    /// The save keystroke itself can be refused, and the takeover has to behave as if that were a
    /// failed save rather than a succeeded one.
    ///
    /// <see cref="ITakeoverActions.RequestSave"/> returns a verdict instead of throwing precisely so
    /// that this case exists: the session was activated, and by the time Ctrl+S goes out something
    /// else is in front, so the keystroke that was meant to save an unsaved set would instead land
    /// in whatever the user is typing in. A takeover that asked for the save and never checked the
    /// answer would then go on to believe the set was saved - and the policy allows a kill of a set
    /// it believes is saved. That is the one path from a stolen foreground to a lost session.
    ///
    /// The verdict here is not invented: it is what the real guard says about this fake window, on
    /// this desktop, at this moment - so the sentence the user is shown is the one a real takeover
    /// would log, word for word.
    /// </summary>
    [Fact]
    public async Task ASaveKeystrokeThatWasRefusedLeavesTheSessionOpen()
    {
        var before = InputSimulator.InjectionAttempts;
        using var harness = new Harness("TakeoverFixture Refused* - Ableton Live 12 Suite");

        // The same call the production actions make. The fake session's window is never activated,
        // so this is a real refusal produced by the real guard - and asking it sends nothing.
        Assert.NotNull(GuardedInput.RefusalReason(harness.Session.MainHandle));
        var verdict = GuardedInput.PressChord(
            harness.Session.MainHandle, InputSimulator.VkControl, InputSimulator.VkS, "asked Live to save");

        Assert.False(verdict.Injected, "the fake session somehow took the foreground during the suite");
        harness.Actions.SaveVerdict = verdict;

        var result = await harness.CloseAsync(Timing(closeBudgetMs: 700, graceMs: 400));

        AssertOutcome(SessionClose.Refused, result, harness);
        Assert.Equal(SaveState.Failed, result.Save);

        // The refusal is carried to the user rather than flattened into "the save failed": which
        // window was in front is the thing they have to go and close, and the reason code is what
        // makes a batch that refused five times greppable.
        Assert.Contains($"the save keystroke was refused ({verdict.Reason})", result.Message);
        Assert.Contains(verdict.Detail, result.Message);
        Assert.Contains("TakeoverFixture Refused", result.Message);
        Assert.Contains("nothing was closed", result.Message);

        // The keystroke went nowhere, so nothing was asked of the session and nothing was ended.
        Assert.Equal(1, harness.Actions.Saves);
        Assert.Equal(harness.Session.MainHandle, harness.Actions.SaveTarget);
        Assert.Equal(0, harness.Actions.Closes);
        Assert.Equal(0, harness.Actions.Terminations);
        Assert.True(WindowControl.IsVisible(harness.Session.MainHandle), "the session must be left open");
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }
}

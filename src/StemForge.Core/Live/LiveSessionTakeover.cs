using System.Diagnostics;
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
/// Closes every Ableton Live instance so the batch can drive its own.
///
/// Live is single-instance and there is no documented way to hand a set to one that is already
/// running, so a run has to own the process outright. The contract with the user is therefore
/// "a run takes over Live", not "a run refuses to start while Live is open" - and taking over
/// means saving what was open first, because the alternative is losing somebody's afternoon.
///
/// Three rules keep this from becoming the thing that hangs:
///   * every wait is bounded, and the last resort is a process kill;
///   * a button is only pressed when its label matches exactly, because a substring match on
///     "No" also matches "Monitor" and the wrong click here is worse than no click;
///   * nothing is ever overwritten: an unsaved set goes to the recovery folder under a name
///     that did not exist a moment earlier.
/// </summary>
public static class LiveSessionTakeover
{
    private static readonly TimeSpan SaveDialogGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CloseBudget = TimeSpan.FromSeconds(25);
    private const int MaxPromptsPerSession = 3;

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

    /// <summary>Cancel labels, matched exactly.</summary>
    private static readonly string[] CancelLabels =
    {
        "Cancel", "Close",
    };

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
    /// Saves and closes every running Live. Returns how many are gone; a session that cannot be
    /// reached is logged and skipped rather than failing the run, because the caller is about to
    /// launch its own Live and a stale instance is a visible problem, not a silent one.
    /// </summary>
    public static async Task<int> CloseAllAsync(RunLog log, Job? job = null, CancellationToken ct = default)
    {
        var running = LiveProcessWindows.FindLiveProcesses();
        if (running.Count == 0) return 0;

        var launchedByUs = LiveLaunchRecord.ReadCurrent();
        var closed = 0;

        foreach (var proc in running)
        {
            using (proc)
            {
                try
                {
                    if (await CloseSessionAsync(proc, launchedByUs?.ProcessId == proc.Id, log, job, ct).ConfigureAwait(false))
                        closed++;
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

        LiveLaunchRecord.Clear();
        log.Info($"took over {closed} of {running.Count} Ableton Live session(s)", job);
        return closed;
    }

    /// <summary>
    /// Saves and closes one Live session that the caller already knows about. Used when the
    /// runner is putting its own working session away, which raises the same prompts as taking
    /// somebody else's over.
    /// </summary>
    public static Task<bool> CloseAsync(Process proc, RunLog log, Job? job = null, CancellationToken ct = default)
    {
        var launchedByUs = LiveLaunchRecord.ReadCurrent()?.ProcessId == proc.Id;
        return CloseSessionAsync(proc, launchedByUs, log, job, ct);
    }

    private static async Task<bool> CloseSessionAsync(
        Process proc, bool launchedByUs, RunLog log, Job? job, CancellationToken ct)
    {
        if (!LiveProcessWindows.IsAlive(proc.Id)) return true;

        var title = WindowControl.GetMainWindowTitle(proc);
        var kind = LiveSessionClassifier.Classify(title, launchedByUs);
        var session = new RunningLiveSession(proc.Id, title, kind);
        log.Info($"closing Live session: {session.Description}", job);

        var main = LiveProcessWindows.MainWindow(proc.Id)?.Handle ?? IntPtr.Zero;

        if (main != IntPtr.Zero && WindowControl.Activate(main))
        {
            await SaveOpenSetAsync(proc, main, log, job, ct).ConfigureAwait(false);
            CloseRequest.CloseWindow(main);
        }
        else
        {
            log.Warn($"Live (pid {proc.Id}) has no window to save into; it will be terminated", job);
        }

        if (await WaitForExitAsync(proc, main, log, job, ct).ConfigureAwait(false)) return true;

        log.Warn($"Live (pid {proc.Id}) did not close on its own; terminating it", job);
        try
        {
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            log.Warn($"Live (pid {proc.Id}) refused to be terminated: {ex.Message}", job);
        }

        return !LiveProcessWindows.IsAlive(proc.Id);
    }

    /// <summary>
    /// Ctrl+S into the focused session. A named set is written where it lives and no dialog
    /// appears; a set that was never saved opens the file dialog, which gets a recovery name
    /// rather than a default one, because the default is "Untitled.als" in whoever's documents
    /// folder and, worse, may overwrite the last such set.
    /// </summary>
    private static async Task SaveOpenSetAsync(Process proc, IntPtr main, RunLog log, Job? job, CancellationToken ct)
    {
        try
        {
            InputSimulator.Chord(InputSimulator.VkControl, InputSimulator.VkS);
        }
        catch (Exception ex)
        {
            log.Warn($"could not send save to Live (pid {proc.Id}): {ex.Message}", job);
            return;
        }

        var dialog = await WaitForDialogAsync(proc, main, SaveDialogGrace, ct).ConfigureAwait(false);
        if (dialog is null) return;

        var path = RecoveryPath(WindowControl.GetTitle(main), AppPaths.RecoveryRoot, DateTimeOffset.Now);

        if (TryNameFileDialog(dialog, path))
        {
            log.Info($"saved the unset set to {path}", job);
            return;
        }

        // A dialog whose file name cannot be set is a dialog whose outcome cannot be predicted,
        // so it is cancelled and the changes are dropped on close. Predictable beats lucky.
        Press(dialog, CancelLabels, fallBackToEscape: true);
        log.Warn($"the save dialog of Live (pid {proc.Id}) could not be named; the unsaved set is discarded", job);
        await Task.Delay(300, ct).ConfigureAwait(false);
    }

    /// <summary>Waits for the save dialog that Ctrl+S may raise, or returns null when none does.</summary>
    private static async Task<TopLevelWindow?> WaitForDialogAsync(
        Process proc, IntPtr main, TimeSpan budget, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + budget;

        while (DateTime.UtcNow < deadline)
        {
            var dialog = LiveProcessWindows.OtherVisibleWindows(proc.Id, main).FirstOrDefault();
            if (dialog is not null) return dialog;

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Writes the recovery path into a file dialog and confirms it. Both steps have to work,
    /// or nothing is confirmed at all.
    /// </summary>
    private static bool TryNameFileDialog(TopLevelWindow dialog, string path)
    {
        var root = LiveUi.FromHandle(dialog.Handle);
        if (root is null) return false;

        var edit = LiveUi.FindFileNameEdit(root);
        if (edit is null) return false;
        if (!LiveUi.TrySetValue(edit, path)) return false;

        // Enter is the dialog's own default action and is used when no confirm button can be
        // matched by name - pressing a button found by guessing is how the wrong one gets clicked.
        var confirm = FindButton(root, ConfirmLabels);
        if (confirm is not null && LiveUi.TryInvoke(confirm)) return true;

        try
        {
            WindowControl.Activate(dialog.Handle);
            InputSimulator.KeyPress(InputSimulator.VkReturn);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Watches the session out the door, answering whatever it puts up on the way. A prompt here
    /// is answered by dropping changes: the save, if there was going to be one, already happened,
    /// and clicking Save again only opens another dialog to negotiate with.
    /// </summary>
    private static async Task<bool> WaitForExitAsync(
        Process proc, IntPtr main, RunLog log, Job? job, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + CloseBudget;
        var answered = 0;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!LiveProcessWindows.IsAlive(proc.Id)) return true;

            if (answered < MaxPromptsPerSession)
            {
                var dialog = LiveProcessWindows.OtherVisibleWindows(proc.Id, main).FirstOrDefault();
                if (dialog is not null)
                {
                    answered++;
                    log.Warn($"Live (pid {proc.Id}) put up '{dialog.Title}' while closing; answering it", job);
                    Press(dialog, DiscardLabels, fallBackToEscape: true);
                }
            }

            await Task.Delay(300, ct).ConfigureAwait(false);
        }

        return !LiveProcessWindows.IsAlive(proc.Id);
    }

    private static void Press(TopLevelWindow dialog, string[] labels, bool fallBackToEscape)
    {
        var root = LiveUi.FromHandle(dialog.Handle);
        var button = root is null ? null : FindButton(root, labels);

        if (button is not null && LiveUi.TryInvoke(button)) return;

        if (!fallBackToEscape) return;

        try
        {
            WindowControl.Activate(dialog.Handle);
            InputSimulator.KeyPress(InputSimulator.VkEscape);
        }
        catch
        {
            // Nothing else to do; the caller's deadline and kill cover it.
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
    public static AutomationElement? FindButton(AutomationElement root, string[] labels)
    {
        var candidates = new List<(string Name, AutomationElement Element)>();

        foreach (var element in LiveUi.Descendants(root))
        {
            try
            {
                var type = element.Current.ControlType;
                if (type != ControlType.Button && type != ControlType.MenuItem) continue;

                var name = LiveUi.Name(element).Trim();
                if (name.Length > 0) candidates.Add((name, element));
            }
            catch
            {
                // An element that cannot be read is not a button worth pressing.
            }
        }

        foreach (var label in labels)
            foreach (var (name, element) in candidates)
                if (string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
                    return element;

        return null;
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

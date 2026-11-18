using System.Diagnostics;
using StemForge.Core.Engines;
using StemForge.Core.Live.Automation;
using StemForge.Core.Live.Projects;
using StemForge.Core.Logging;
using StemForge.Core.Models;
using StemForge.Core.Support;

namespace StemForge.Core.Live;

/// <summary>Raised internally when automation must stop; carries an actionable reason.</summary>
internal sealed class LiveAutomationException : Exception
{
    public ErrorKind Kind { get; }

    public LiveAutomationException(ErrorKind kind, string message) : base(message) => Kind = kind;
}

/// <summary>
/// Drives the installed Ableton Live build through one stem separation.
///
/// Design rules, all of which protect the user's machine and their Live install:
///   * Live is only ever given the working set this tool generated. A session that is already
///     open is saved and closed before the run starts, because Live cannot be handed a set while
///     it is running - see <see cref="LiveSessionTakeover"/>.
///   * Input is only sent after the Live window, foreground state and desktop are verified.
///   * Every wait is bounded and every stage reports progress.
///   * A file-specific failure is skipped only after Live is back in a known state; an
///     unknown state pauses the batch instead of attempting blind recovery.
/// </summary>
public sealed class LiveAutomationRunner : ISeparator
{
    private sealed record FileState(long Size, DateTime Write);

    /// <summary>Recent Live temp projects that are also watched for stem output.</summary>
    private const int TempProjectsToWatch = 3;

    private readonly RunLog _log;
    private readonly LiveUiProfile _profile;
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly string _diagnosticsFolder = Path.Combine(AppPaths.LogsFolder, "screenshots");

    public string Name => "Ableton Live (automation)";

    public EngineKind Kind => EngineKind.Live;

    public LiveAutomationRunner(RunLog log, LiveUiProfile? profile = null)
    {
        _log = log;
        _profile = profile ?? LiveUiProfile.Load();
    }

    public async Task<SeparationOutcome> SeparateAsync(SeparationRequest request, CancellationToken cancellationToken)
    {
        Process? process = null;
        var setLoaded = false;
        try
        {
            Report(request, JobStage.CheckingSource);

            // The support gate is version AND edition, and it is asked at the very top: above the
            // desktop read and far above ClearLiveAsync. The alternative is learning that this Live
            // cannot separate stems from a greyed-out Create menu, which is after the run has saved
            // and closed the user's own session to get there - the worst possible place to find out
            // a precondition. This position is load-bearing, so it is the first check in the method.
            var support = request.Install.Support;
            if (!support.IsSupported)
            {
                // Both halves of the gate are reported as one kind. That is a modelling choice, not
                // a cost: a queue file names these causes, every place that reads an ErrorKind has a
                // default arm, and nothing reads the numbers at all - so a member for "the right
                // Live, the wrong box" would cost one line. It is left out because the sentence and
                // the fix are the same either way (this Live cannot separate stems; use one that
                // can), and because the reason token says which half refused wherever it is read:
                // it leads the warning below and trails the thrown text, via SupportVerdict.
                // FailureText.
                _log.Warn($"{support.Reason}: {support.Sentence} Nothing was closed or saved.", request.Job);
                throw new LiveAutomationException(ErrorKind.LiveVersionUnsupported, support.FailureText);
            }

            DesktopGuard.Require(string.Empty);

            // Whoever is looking at the screen when the batch starts is who gets it back between
            // tracks: the run only needs the foreground to click.
            var screenOwner = WindowControl.GetForegroundWindow();

            _log.Info($"Live install: {request.Install.DisplayName} ({request.Install.ExePath})", request.Job);

            if (!File.Exists(request.BaseTemplatePath))
                throw new LiveAutomationException(ErrorKind.LiveVersionUnsupported,
                    "Live's audio-track template could not be found, so the working project cannot be built.");

            await ClearLiveAsync(request, cancellationToken).ConfigureAwait(false);

            Report(request, JobStage.PreparingProject);
            var spec = new AudioClipSpec(
                request.Source.Path,
                request.Source.SizeBytes,
                request.SourceInfo.SampleFrames,
                request.SourceInfo.SampleRate,
                Path.GetFileNameWithoutExtension(request.Source.Path));

            var projectPath = WorkingProjectBuilder.CreateProject(request.WorkingFolder, request.BaseTemplatePath, spec);
            _log.Info($"working project: {projectPath}", request.Job);

            var baseline = SnapshotStems(request);
            _log.Info($"stem baseline: {baseline.Count} pre-existing file(s)", request.Job);

            Report(request, JobStage.LaunchingLive);
            process = LaunchLive(request.Install.ExePath, projectPath);
            _log.Info($"launched Live (pid {process.Id})", request.Job);
            LiveLaunchRecord.Write(process, request.Install.ExePath);

            Report(request, JobStage.WaitingForLive);
            var mainWindow = await WaitForMainWindowAsync(process, request, cancellationToken).ConfigureAwait(false);

            // Killed before it can swallow a command: the scan Live starts over the library on
            // launch is the one thing that makes it ignore the Create menu.
            PauseLiveAnalysis(mainWindow, request);

            Report(request, JobStage.OpeningProject);
            await WaitForSetLoadedAsync(process, mainWindow, request, cancellationToken).ConfigureAwait(false);
            setLoaded = true;
            LiveLaunchRecord.Clear();

            Report(request, JobStage.SelectingClip);
            SelectClip(mainWindow, spec, request);

            Report(request, JobStage.OpeningStemDialog);
            var dialog = await OpenSeparateStemsDialogAsync(process, mainWindow, spec, request, cancellationToken).ConfigureAwait(false);

            Report(request, JobStage.ConfiguringStemDialog);
            ConfigureDialog(dialog, request);

            // The Separate button is dropped exactly like the menu command is while Live is busy,
            // so the same precondition applies before the click that actually starts the work.
            await WaitForLiveToSettleAsync(mainWindow, request, JobStage.ConfiguringStemDialog, cancellationToken).ConfigureAwait(false);

            Report(request, JobStage.Separating);
            var stopwatch = Stopwatch.StartNew();
            ClickSeparate(process, dialog, request);

            // Everything Live needs next is a file on disk, and that takes minutes. The screen is
            // only wanted again for the next track, so it goes back to whoever had it.
            YieldScreen(screenOwner, process, request);

            await WaitForSeparationAsync(process, mainWindow, request, baseline, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var separationSeconds = stopwatch.Elapsed.TotalSeconds;
            _log.Info($"separation completed in {separationSeconds:F1}s", request.Job);

            Report(request, JobStage.CollectingStems);
            var generated = CollectGeneratedStems(request);
            if (generated.Count == 0)
            {
                return SeparationOutcome.Failure(ErrorKind.StemsMissing,
                    "Live finished but no stem files were produced.", separationSeconds);
            }

            return new SeparationOutcome(true, ErrorKind.None, null, generated, separationSeconds, false);
        }
        catch (OperationCanceledException)
        {
            _log.Warn("automation cancelled", request.Job);
            return SeparationOutcome.Cancelled(ErrorKind.Cancelled, "Stopped before the next track.");
        }
        catch (LiveAutomationException ex)
        {
            _log.Error(ex.Message, request.Job);
            CaptureDiagnostic(process, request, ex.Kind.ToString());
            return SeparationOutcome.Failure(ex.Kind, ex.Message);
        }
        catch (Exception ex)
        {
            _log.Error($"unexpected automation failure: {ex}", request.Job);
            CaptureDiagnostic(process, request, "unexpected");
            return SeparationOutcome.Failure(ErrorKind.Unknown, ex.Message);
        }
        finally
        {
            // An aborted launch must not leave a Live behind that a later run would mistake
            // for the user's own session.
            if (process is not null && !setLoaded)
            {
                await CloseLiveAsync(process, CancellationToken.None).ConfigureAwait(false);
                LiveLaunchRecord.Clear();
            }

            process?.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Preflight: take whatever Live instances exist off the machine
    // ------------------------------------------------------------------

    /// <summary>
    /// A run needs Live to itself, so any Live that is already open is saved and closed here.
    /// Refusing to start was the previous behaviour, and it turned one abandoned launch into a
    /// queue that could never be resumed; taking over is the version that finishes unattended.
    /// </summary>
    private async Task ClearLiveAsync(SeparationRequest request, CancellationToken ct)
    {
        await LiveSessionTakeover.CloseAllAsync(_log, request.Job, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Launch and window handling
    // ------------------------------------------------------------------

    private static Process LaunchLive(string exePath, string projectPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "\"" + projectPath + "\"",
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };

        return Process.Start(startInfo)
            ?? throw new LiveAutomationException(ErrorKind.LiveStartupFailed, "Could not start Ableton Live.");
    }

    private async Task<IntPtr> WaitForMainWindowAsync(Process process, SeparationRequest request, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(_profile.LiveStartupTimeoutSeconds);
        IntPtr handle = IntPtr.Zero;
        var answered = new Dictionary<IntPtr, int>();
        var described = new HashSet<IntPtr>();

        while (DateTime.UtcNow < deadline)
        {
            CheckAbort(process, request, interacting: false);

            var main = LiveProcessWindows.MainWindow(process.Id);
            if (main is not null)
            {
                handle = main.Handle;
                break;
            }

            // Live can stop on a prompt of its own before it ever shows a titled window - the
            // "Live unexpectedly quit, would you like to recover your work?" question after a
            // previous run had to terminate it is exactly that. Waiting for a main window that
            // the modal is preventing is not a wait, it is a deadlock, so the modal is answered
            // here rather than at some later stage.
            await AnswerStartupPromptAsync(process, request, answered, described, ct).ConfigureAwait(false);

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        if (handle == IntPtr.Zero)
            throw new LiveAutomationException(ErrorKind.LiveWindowNotFound, "Ableton Live did not open a window in time.");

        if (!WindowControl.TryActivate(handle))
        {
            var thief = WindowControl.GetTitle(WindowControl.GetForegroundWindow());
            throw new LiveAutomationException(ErrorKind.LiveLostFocus,
                "Could not focus the Ableton Live window" +
                (string.IsNullOrWhiteSpace(thief) ? "." : $": '{thief}' holds the foreground."));
        }

        WindowControl.Maximize(handle);
        await Task.Delay(500, ct).ConfigureAwait(false);
        return handle;
    }

    /// <summary>
    /// The labels that are safe to press on a window nothing is known about. "Close" is
    /// deliberately absent: Live's own main window has no title while it is still loading, and
    /// its title bar exposes a Close button, so a broader set would answer the launch by
    /// shutting the application down.
    /// </summary>
    private static readonly string[] StartupPromptLabels =
    [
        "OK", "Don't Recover", "Don’t Recover", "Dont Recover", "No", "Cancel",
    ];

    /// <summary>
    /// How many times one startup window may be pressed before it is given up on. The budget is
    /// spent on presses, not on glances: while Live is still initialising, a modal's own buttons
    /// are not in the accessibility tree yet, and the window that offered nothing on the first
    /// poll is the same window that offers "No" on the tenth. Counting glances is what let the
    /// recovery prompt sit in front of the main window for the whole startup timeout. Six rounds
    /// cost about half a minute of the three minutes a startup is allowed, which is the time an
    /// app that keeps reasserting its window takes to let go.
    /// </summary>
    private const int StartupPromptAttempts = 6;

    /// <summary>
    /// Clicks the safe button of a Live window that has no title, which is what a startup modal
    /// looks like from outside. Only a button whose label is in <see cref="StartupPromptLabels"/>
    /// is touched, matched exactly; a window that offers none of those is left alone rather than
    /// poked with Escape, because at this point nothing is known about it.
    /// </summary>
    private async Task AnswerStartupPromptAsync(
        Process process,
        SeparationRequest request,
        Dictionary<IntPtr, int> answered,
        HashSet<IntPtr> described,
        CancellationToken ct)
    {
        foreach (var window in LiveProcessWindows.ForProcess(process.Id)
                     .Where(w => w.Visible && string.IsNullOrWhiteSpace(w.Title)))
        {
            // A prompt is only given up on once it has been pressed and refused to go away. A
            // window that has not built its buttons yet costs nothing and is looked at again.
            answered.TryGetValue(window.Handle, out var presses);
            if (presses >= StartupPromptAttempts)
            {
                if (described.Add(window.Handle))
                {
                    _log.Warn($"Live's startup window is still up after {presses} presses; " +
                              "waiting it out", request.Job);
                }
                continue;
            }

            var root = LiveUi.FromHandle(window.Handle);
            IReadOnlyList<DialogButton> buttons = root is null
                ? Array.Empty<DialogButton>()
                : LiveSessionTakeover.CollectButtons(root);

            if (LiveSessionTakeover.Pick(buttons, StartupPromptLabels) is not { } target)
            {
                if (described.Add(window.Handle) && root is not null)
                {
                    // What the modal offered is the only record of why it was not answered.
                    var offered = string.Join(", ", buttons.Select(b => b.Name).Take(8));
                    _log.Warn($"Live showed an untitled startup window offering [{offered}]", request.Job);
                }
                continue;
            }

            var label = target.Name;

            // The key first, because it costs the user nothing: it goes into this dialog's own
            // queue, so it needs neither the foreground nor the screen, and there is no moment in
            // which a click aimed at Live's box lands on his window instead. Measured on the
            // recovery question - a posted 'N' answered it and Live carried on loading.
            //
            // A press is spent on an attempt that reached the window, not on a round that found a
            // button. The six answers are what a stubborn modal gets; a foreground held by a toast
            // for two seconds used to take one of them away, and the modal that was never raised was
            // charged as if it had been.
            if (DialogKey.ForLabel(label, buttons.Select(b => b.Name)) is { } key
                && InputSimulator.PostKey(window.Handle, key))
            {
                answered[window.Handle] = presses + 1;

                if (await WaitUntilGoneAsync(process, IntPtr.Zero, window.Handle, ct).ConfigureAwait(false))
                {
                    _log.Warn($"answered Live's startup prompt with '{label}' by key, without taking " +
                              "the screen, before the main window appeared", request.Job);
                    return;
                }
            }

            // A click lands on whatever is topmost at that point, so a modal that has not been
            // raised is a modal that cannot be answered - and what sits on top of it is often
            // another Live window, which is why the strict question is asked here: *this* modal in
            // front, not just any window of Live's. Refusing to raise it costs no press; the next
            // poll looks again, because a foreground held by a toast is only held for a moment.
            var raised = WindowControl.TryActivateAndReport(window.Handle, attempts: 2);
            if (!raised.TargetIsForeground)
            {
                if (described.Add(window.Handle))
                {
                    _log.Warn($"Live's startup prompt would not come to the front; " +
                              $"{raised.Describe(window.Handle)}", request.Job);
                }
                continue;
            }

            // Raise, click, read back - in that order and with nothing in between. The recovery
            // question exposes InvokePattern and does nothing when it is called, so the call is
            // not even attempted here; and the three seconds that a verification wait costs are
            // three seconds in which another app - measured here, an Electron window that keeps
            // reasserting itself - takes the screen back and the click lands on it instead.
            answered[window.Handle] = presses + 1;

            if (LiveUi.TryClick(target.Element)
                && await WaitUntilGoneAsync(process, IntPtr.Zero, window.Handle, ct).ConfigureAwait(false))
            {
                _log.Warn($"answered Live's startup prompt with '{label}' before the main window appeared",
                    request.Job);
                return;
            }

            _log.Warn($"Live's startup prompt is still up after clicking '{label}' " +
                      $"({presses + 1}/{StartupPromptAttempts}); the foreground belongs to " +
                      $"'{ForegroundHolder()}'", request.Job);
        }
    }

    private static string ForegroundHolder() =>
        WindowControl.GetTitle(WindowControl.GetForegroundWindow());

    private async Task WaitForSetLoadedAsync(Process process, IntPtr mainWindow, SeparationRequest request, CancellationToken ct)
    {
        var projectName = Path.GetFileNameWithoutExtension(request.ProjectFilePath);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(_profile.SetLoadTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            CheckAbort(process, request, interacting: true);

            if (!await HandleUnexpectedDialogsAsync(process, mainWindow, request, ct).ConfigureAwait(false))
                throw new LiveAutomationException(ErrorKind.UnexpectedDialog,
                    "Ableton Live showed a dialog that Skeer autoSTEM could not dismiss while the " +
                    "working set was loading. Dismiss it, then resume the batch.");

            var title = WindowControl.GetTitle(mainWindow);
            if (title.Contains(projectName, StringComparison.OrdinalIgnoreCase))
            {
                // Give Live a moment to settle after the title updates.
                await Task.Delay(1500, ct).ConfigureAwait(false);
                return;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new LiveAutomationException(ErrorKind.LiveStartupFailed,
            "The working project did not finish loading in Ableton Live.");
    }

    /// <summary>
    /// Live puts up prompts of its own - recovery after an unclean shutdown, missing devices or
    /// samples, a set that wants saving. Each one is answered with a label matched exactly, or
    /// with Escape. Returns false when a prompt is still on screen afterwards, which is the
    /// caller's cue to stop waiting on it; a batch is not left behind a dialog for somebody to
    /// come back and click.
    /// </summary>
    private async Task<bool> HandleUnexpectedDialogsAsync(Process process, IntPtr mainWindow, SeparationRequest request, CancellationToken ct)
    {
        var dialog = LiveProcessWindows.OtherVisibleWindows(process.Id, mainWindow).FirstOrDefault();
        if (dialog is null) return true;

        if (IsExpectedWindowTitle(dialog.Title))
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
            return true;
        }

        _log.Warn($"unexpected Live dialog '{dialog.Title}'", request.Job);
        CaptureDiagnostic(process, request, "unexpected-dialog");

        if (await DismissAsync(process, mainWindow, dialog, request, ct).ConfigureAwait(false))
        {
            _log.Warn($"dismissed dialog '{dialog.Title}'", request.Job);
            return true;
        }

        _log.Warn($"dialog '{dialog.Title}' is still on screen after being answered", request.Job);
        return false;
    }

    /// <summary>
    /// Labels that get rid of a prompt without committing to anything it offers, in preference
    /// order: the answer to a question first, the window's own furniture last.
    /// </summary>
    private static readonly string[] DismissLabels =
    {
        "OK", "Don't Recover", "Don’t Recover", "Dont Recover", "Discard",
        "Don't Save", "Don’t Save", "Dont Save", "No", "Cancel", "Close",
    };

    private async Task<bool> DismissAsync(
        Process process, IntPtr mainWindow, TopLevelWindow dialog, SeparationRequest request, CancellationToken ct)
    {
        var root = LiveUi.FromHandle(dialog.Handle);
        IReadOnlyList<DialogButton> buttons = root is null
            ? Array.Empty<DialogButton>()
            : LiveSessionTakeover.CollectButtons(root);

        if (LiveSessionTakeover.Pick(buttons, DismissLabels) is { } target)
        {
            // Same ladder as the takeover's: the key that reaches this dialog's queue needs no
            // screen, and only if Live ignores it does the run go looking for the foreground.
            if (DialogKey.ForLabel(target.Name, buttons.Select(b => b.Name)) is { } key
                && InputSimulator.PostKey(dialog.Handle, key)
                && await WaitUntilGoneAsync(process, mainWindow, dialog.Handle, ct).ConfigureAwait(false))
            {
                return true;
            }

            // A click, not Invoke: Live's boxes report a programmatic Invoke as success and change
            // nothing, which is how a dialog came back here every round of the wait.
            try
            {
                if (LiveUi.TryClick(target.Element))
                    return await WaitUntilGoneAsync(process, mainWindow, dialog.Handle, ct)
                        .ConfigureAwait(false);
            }
            catch
            {
                // There was no desktop to click on; Escape below is the answer that needs none.
            }
        }

        try
        {
            WindowControl.Activate(dialog.Handle);

            // Guarded, because "Activate" is a request and not a fact: if the dialog did not come
            // forward, Escape belongs to whatever did, and an Escape sent there is this product
            // pressing keys in the user's window. Refusing costs a dialog that stays on screen,
            // which the caller below already reports.
            var escape = GuardedInput.PressKey(dialog.Handle, InputSimulator.VkEscape,
                $"escaped '{dialog.Title}'");

            if (!escape.Injected) _log.Warn($"{escape.Sentence}", request.Job);
        }
        catch
        {
            return false;
        }

        return await WaitUntilGoneAsync(process, mainWindow, dialog.Handle, ct).ConfigureAwait(false);
    }

    private static async Task<bool> WaitUntilGoneAsync(
        Process process, IntPtr mainWindow, IntPtr handle, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var stillThere = LiveProcessWindows.ForProcess(process.Id)
                .Any(window => window.Handle == handle && window.Visible);
            if (!stillThere) return true;

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// The windows the run itself opened. Anything else that belongs to Live is a prompt to be
    /// answered, not a state to be waited out - which is why the window class is not simply
    /// "everything Live owns", even though that would be quieter.
    /// </summary>
    private static bool IsExpectedWindowTitle(string title) =>
        title.Contains("Separate", StringComparison.OrdinalIgnoreCase)
        || title.Contains("Stem", StringComparison.OrdinalIgnoreCase)
        || title.Contains("Progress", StringComparison.OrdinalIgnoreCase)
        || title.Contains("autoSTEM", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // Clip selection and the Separate Stems dialog
    // ------------------------------------------------------------------

    private void SelectClip(IntPtr mainWindow, AudioClipSpec spec, SeparationRequest request)
    {
        FocusLive(mainWindow);

        var root = LiveUi.FromHandle(mainWindow);
        if (root is not null)
        {
            // Live exposes the session clip slot as a Group whose name is
            // "<clip name>, stopped, in track <track>, scene <n>". Measured against Live 12.3.5,
            // that Group carries no selection pattern, so the only honest way to select it is the
            // click a person would make, on the rectangle Live itself reports for the slot.
            var clip = LiveUi.WaitFor(() => LiveUi.FindClipSlot(root, spec.ClipName), TimeSpan.FromSeconds(5));
            if (clip is not null && LiveUi.TryClick(clip))
            {
                _log.Info($"clicked the clip slot '{LiveUi.Name(clip)}' to select it", request.Job);

                // The slot's own rectangle, restated as the fallback's units. ClipYFraction is
                // stale on this machine - three clicks at 0.62 selected nothing - and the honest
                // way to re-earn it is to write down where Live says the clip is, not to measure a
                // screenshot that may have been rendered at another scale.
                var slot = LiveUi.ClickablePoint(clip);
                var window = WindowControl.GetRect(mainWindow);
                if (slot is not null && window is not null && window.Width > 0 && window.Height > 0)
                {
                    _log.Info($"the clip lives at window-relative " +
                              $"{(slot.Value.X - window.X) / (double)window.Width:F3}," +
                              $"{(slot.Value.Y - window.Y) / (double)window.Height:F3} " +
                              "(ui-profile.json's ClipXFraction, ClipYFraction)", request.Job);
                }

                Thread.Sleep(300);
                return;
            }

            var near = clip is null ? string.Join(", ", LiveUi.Descendants(root)
                    .Select(LiveUi.Name)
                    .Where(n => n.Contains(spec.ClipName, StringComparison.OrdinalIgnoreCase)
                                && n.Length <= spec.ClipName.Length + 60)
                    .Distinct()
                    .Take(6))
                : string.Empty;

            _log.Warn($"the clip slot for '{spec.ClipName}' was not found through UI Automation" +
                      (near.Length == 0 ? "" : $"; what Live does name after it: [{near}]"), request.Job);
        }

        var rect = WindowControl.GetRect(mainWindow)
            ?? throw new LiveAutomationException(ErrorKind.LiveWindowNotFound, "The Live window disappeared.");

        var (x, y) = rect.FractionPoint(_profile.ClipXFraction, _profile.ClipYFraction);

        _log.Info($"selecting clip with a window-relative click at {x},{y}", request.Job);

        // Re-verified inside the guard, and that is the point: Live was focused above, then UI
        // Automation spent up to five seconds looking for the slot, and whatever the user focused in
        // those seconds is what an unguarded click at these coordinates would have pressed.
        var click = GuardedInput.Click(mainWindow, new System.Drawing.Point(x, y), "selected the clip slot");
        if (!click.Injected)
            throw new LiveAutomationException(click.Kind,
                $"Skeer autoSTEM could not find the clip slot through Live's accessibility tree and " +
                $"then could not click where its window geometry says the clip is - it {click.Sentence}. " +
                "Nothing was clicked. Bring Live to the front and resume the batch.");

        Thread.Sleep(350);
    }

    /// <summary>How many times the command is offered to Live before the run calls it impossible.</summary>
    private const int SeparateStemsAttempts = 3;

    /// <summary>What pressing the menu entry achieved, as far as Live is concerned.</summary>
    private enum StemCommand
    {
        /// <summary>The entry was clicked. Whether anything opened as a result is the next stage's question.</summary>
        Chosen,

        /// <summary>Live has no such entry in its tree at the moment.</summary>
        Absent,

        /// <summary>
        /// The entry is there and greyed out. Live refuses the command, and pressing it - by
        /// pattern or by mouse - changes nothing, so repeating the press is not a retry, it is a
        /// waste of the dialog timeout. Used to be reported as success.
        /// </summary>
        GreyedOut,
    }

    /// <summary>
    /// Opens Live's Separate Stems dialog.
    ///
    /// Choosing the menu entry is not enough. While Live is analysing files it accepts the command
    /// and does nothing with it - which is how the batch first stopped: the menu said yes, no
    /// dialog ever appeared, and sixty seconds later the job failed. So every attempt waits for
    /// Live's own work to finish, and a dialog that does not show up is retried rather than being
    /// treated as fatal on the first miss. A greyed-out entry is not retried at all: it is
    /// re-earned by selecting the clip again, and if Live still refuses it the job says so.
    /// </summary>
    private async Task<IntPtr> OpenSeparateStemsDialogAsync(
        Process process, IntPtr mainWindow, AudioClipSpec spec, SeparationRequest request, CancellationToken ct)
    {
        string? lastStatus = null;
        var lastCommand = StemCommand.Absent;

        for (var attempt = 1; attempt <= SeparateStemsAttempts; attempt++)
        {
            await WaitForLiveToSettleAsync(mainWindow, request, JobStage.OpeningStemDialog, ct).ConfigureAwait(false);
            FocusLive(mainWindow);

            lastCommand = TryChooseSeparateStems(process, mainWindow, request, ct);

            if (lastCommand == StemCommand.GreyedOut)
            {
                // Live is saying the clip is not selected in the way its command needs. Select it
                // again, the hard way, and ask once more before blaming Live for it.
                _log.Warn($"attempt {attempt}: Live's Separate Stems command is greyed out, " +
                          "so the clip is being selected again before it is asked for", request.Job);
                CloseOpenMenus(process, mainWindow, request);
                SelectClip(mainWindow, spec, request);

                // Greyed out is the more useful answer of the two, so a retry that managed no
                // more than failing to open the menu does not get to erase it.
                var askedAgain = TryChooseSeparateStems(process, mainWindow, request, ct);
                if (askedAgain != StemCommand.Absent) lastCommand = askedAgain;
            }

            if (lastCommand != StemCommand.Chosen)
            {
                lastStatus = LiveStatusText(mainWindow);
                CloseOpenMenus(process, mainWindow, request);
                continue;
            }

            var dialog = await WaitForStemDialogAsync(process, mainWindow, request, ct).ConfigureAwait(false);
            if (dialog is not null)
            {
                if (attempt > 1) _log.Info($"the Separate Stems dialog opened on attempt {attempt}", request.Job);
                return dialog.Value;
            }

            lastStatus = LiveStatusText(mainWindow);
            _log.Warn($"attempt {attempt}: Live took the command but opened nothing (status: '{lastStatus}')", request.Job);
            CloseOpenMenus(process, mainWindow, request);
        }

        CaptureDiagnostic(process, request, "create-menu");

        if (lastCommand == StemCommand.GreyedOut)
            throw new LiveAutomationException(ErrorKind.ClipNotSelected,
                "Ableton Live has 'Separate Stems to New Audio Tracks' in its Create menu but keeps it " +
                "greyed out, which is Live's way of saying no audio clip is selected. Skeer autoSTEM " +
                "clicked the clip slot and Live still refuses the command.");

        throw new LiveAutomationException(ErrorKind.LiveErrorDialog,
            $"Live would not open the Separate Stems dialog after {SeparateStemsAttempts} attempts" +
            (string.IsNullOrWhiteSpace(lastStatus) ? "." : $" - Live's status line said '{lastStatus}'."));
    }

    /// <summary>
    /// Chooses Create > Separate Stems and reports what Live allowed.
    ///
    /// The how is <see cref="CreateMenuChooser"/>'s business: several ways of pressing the menu,
    /// none of them believed until the popup has been read back out of Live's own tree. This
    /// method only decides which of those outcomes Live is offering - see
    /// <see cref="StemCommand"/>, and note that whether the entry is enabled is asked before it
    /// is touched, because that is Live's own answer to "can this job run right now".
    /// </summary>
    private StemCommand TryChooseSeparateStems(
        Process process, IntPtr mainWindow, SeparationRequest request, CancellationToken ct)
    {
        var root = LiveUi.FromHandle(mainWindow);
        var create = root is null ? null : LiveUi.FindMenuBarItem(root, "Create");

        var attempts = new List<(string Name, Action Attempt)>();
        void Try(string name, Action action) =>
            attempts.Add((name, () =>
            {
                ct.ThrowIfCancellationRequested();
                action();
            }));

        if (create is not null)
        {
            // Live reports this rectangle itself, so it is the only aim that needs no tuning.
            Try("Live's own rectangle", () => LiveUi.TryClick(create));
            Try("the menu pattern", () => LiveUi.TryOpenMenu(root!, "Create"));
        }

        var rect = WindowControl.GetRect(mainWindow);
        if (rect is not null)
        {
            var (x, y) = rect.FractionPoint(_profile.CreateMenuXFraction, _profile.MenuBarYFraction);

            // The last resort, aimed at the window rather than the desktop: the guard re-reads the
            // foreground here, after the two UIA attempts above have spent their patience waiting for
            // a popup that never opened. A refused attempt is not a failure - the chooser reads the
            // popup back and simply reports that this strategy did not bring the menu up.
            Try($"the window-relative point {x},{y}", () =>
            {
                var click = GuardedInput.Click(
                    mainWindow, new System.Drawing.Point(x, y), "opened Live's Create menu by point");

                if (!click.Injected) _log.Warn(click.Sentence, request.Job);
            });
        }

        var result = CreateMenuChooser.Choose(
            attempts,
            () => ObserveCreateMenu(process, ct),
            () => PressSeparateStems(process, request),
            message => _log.Info(message, request.Job));

        if (result == MenuChoiceResult.PressFailed) CloseOpenMenus(process, mainWindow, request);

        return result switch
        {
            MenuChoiceResult.Chosen => StemCommand.Chosen,
            MenuChoiceResult.GreyedOut => StemCommand.GreyedOut,
            _ => StemCommand.Absent,
        };
    }

    /// <summary>
    /// Reads the Create menu back out of Live's tree: the only evidence that counts that an
    /// attempt to open it actually opened it.
    ///
    /// Scoped to Live's own process, and cancellable on every round. It used to fall back to
    /// searching the whole desktop, which on a busy machine is a several-second walk per attempt -
    /// six of them per menu open - that could not be interrupted and could answer with somebody
    /// else's menu item.
    /// </summary>
    private static MenuPopupState ObserveCreateMenu(Process process, CancellationToken ct)
    {
        var item = LiveUi.WaitForMenuItem(process.Id, "separate stems", MenuPopupPatience, ct);
        if (item is null) return MenuPopupState.NotPoppedUp;
        if (LiveUi.IsStale(item)) return MenuPopupState.ClosedAgain;
        return LiveUi.IsEnabled(item) ? MenuPopupState.Pressable : MenuPopupState.GreyedOut;
    }

    /// <summary>
    /// How long a popup is given to appear. Live builds it on its own UI thread, which is the
    /// thread that is also busy importing content, so the patience is what the shipped code
    /// already allowed and shortening it would only turn a slow menu into an absent one.
    /// </summary>
    private static readonly TimeSpan MenuPopupPatience = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Presses the entry the chooser found. A real click is what Live accepts here; if even
    /// that fails the menu is left as it is and the run says so rather than pressing blind.
    ///
    /// "The entry is in the tree" and "the entry is where it can be clicked" are different claims,
    /// and only the second one is checked before the click: an entry whose popup has just closed
    /// still reports the rectangle it had when it was open, and Live's own main window sits under
    /// it. Pressing that rectangle used to select a clip slot in the session view.
    /// </summary>
    private bool PressSeparateStems(Process process, SeparationRequest request)
    {
        var item = LiveUi.FindMenuItemForProcess(process.Id, "separate stems");
        if (item is null)
        {
            _log.Warn("the Create menu had closed by the time its Separate Stems entry was pressed", request.Job);
            return false;
        }

        // Asked for its own sake, so the log says what the guard saw instead of only that the press
        // did not happen. This is the one place a stale popup shows up, and a real Live run is the
        // only machine that will tell us whether it shows up often.
        if (LiveUi.ClickRefusalReason(item) is { } refusal)
        {
            var where = LiveUi.ClickablePoint(item);
            _log.Warn($"the Create menu's Separate Stems entry was not clicked: {refusal}" +
                      (where is null ? "" : $" (its rectangle is at {where.Value.X},{where.Value.Y})"), request.Job);
            return false;
        }

        if (!LiveUi.TryClick(item))
        {
            _log.Warn("the Create menu was open but its Separate Stems entry could not be pressed", request.Job);
            return false;
        }

        return true;
    }

    private async Task<IntPtr?> WaitForStemDialogAsync(Process process, IntPtr mainWindow, SeparationRequest request, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(_profile.DialogTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var dialog = LiveProcessWindows.OtherVisibleWindows(process.Id, mainWindow).FirstOrDefault();
            if (dialog is not null)
            {
                _log.Info($"Separate Stems dialog window: '{dialog.Title}'", request.Job);
                await Task.Delay(500, ct).ConfigureAwait(false);
                return dialog.Handle;
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>The bottom-left status line, which is where Live says what it is busy with.</summary>
    private static string LiveStatusText(IntPtr mainWindow)
    {
        var root = LiveUi.FromHandle(mainWindow);
        var status = root is null ? null : LiveUi.FindByAutomationId(root, "StatusText");
        return status is null ? string.Empty : LiveUi.Name(status);
    }

    /// <summary>Words in that line which mean Live has work of its own in progress.</summary>
    private static readonly string[] BusyStatusPrefixes =
    {
        "analysis", "analysing", "scanning", "warping", "loading", "collecting", "exporting",
        "saving", "importing", "converting", "building", "resampling", "creating", "preparing",
        "rendering", "generating",
    };

    /// <summary>
    /// A scan that is sitting in the status line says so, but a paused one is not work in progress.
    /// Without this the pause below would be a deadlock: 'Analysis: Paused' still starts with
    /// 'analysis', so the waiter would keep waiting for a job that is deliberately stopped.
    /// </summary>
    private static readonly string[] IdleStatusWords = { "paused", "cancelled", "canceled" };

    public static bool IsLiveBusy(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        if (IdleStatusWords.Any(word => status.Contains(word, StringComparison.OrdinalIgnoreCase))) return false;
        return BusyStatusPrefixes.Any(prefix => status.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Presses the pause control next to Live's status line.
    ///
    /// Live has no preference that switches file analysis off - the first pass over a library of
    /// this size simply runs, and while it runs Live accepts the Create command and the dialog's
    /// Separate button and does nothing with either. Its own pause control is the only off switch
    /// that exists, and a paused scan leaves Live idle enough to take commands. The scan resumes on
    /// the next launch; that is the deal Live offers and it costs the batch nothing.
    /// </summary>
    private void PauseLiveAnalysis(IntPtr mainWindow, SeparationRequest request)
    {
        try
        {
            var root = LiveUi.FromHandle(mainWindow);
            var button = root is null ? null : LiveUi.FindByAutomationId(root, "PauseResume");

            if (button is null)
            {
                _log.Info("Live showed no analysis pause control", request.Job);
                return;
            }

            var label = LiveUi.Name(button);
            if (!label.Equals("Pause", StringComparison.OrdinalIgnoreCase))
            {
                _log.Info($"Live's analysis needs no pausing (the control says '{label}')", request.Job);
                return;
            }

            if (LiveUi.TryInvoke(button))
                _log.Info("paused Live's file analysis so the session can take commands", request.Job);
            else
                _log.Warn("Live's analysis pause control could not be pressed", request.Job);
        }
        catch (Exception ex)
        {
            _log.Warn($"could not pause Live's analysis: {ex.Message}", request.Job);
        }
    }

    /// <summary>
    /// Waits for Live to finish its own background work. File analysis over a large library runs
    /// for minutes, and commands given to Live during it are accepted and dropped, so this is not
    /// politeness - it is the precondition for the command landing at all. Running out of the
    /// budget is a warning rather than a failure: the caller still tries, and says what Live was
    /// doing if it does not work.
    /// </summary>
    private async Task WaitForLiveToSettleAsync(
        IntPtr mainWindow, SeparationRequest request, JobStage stage, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(_profile.SettleTimeoutSeconds);
        var since = (DateTime?)null;
        var lastLogged = DateTime.MinValue;
        var triedToPause = false;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var status = LiveStatusText(mainWindow);
            if (!IsLiveBusy(status))
            {
                if (since is not null)
                    _log.Info($"Live finished its own work in {(DateTime.UtcNow - since.Value).TotalSeconds:F0}s", request.Job);
                return;
            }

            since ??= DateTime.UtcNow;
            Report(request, stage, $"Waiting for Ableton Live: {status}");

            // The launch-time pause can miss the window: the status line appears a moment after the
            // main window does. One more press here is what keeps a long scan from eating the run.
            if (!triedToPause)
            {
                triedToPause = true;
                PauseLiveAnalysis(mainWindow, request);
            }

            if ((DateTime.UtcNow - lastLogged).TotalSeconds >= 15)
            {
                _log.Info($"waiting for Live to finish: {status}", request.Job);
                lastLogged = DateTime.UtcNow;
            }

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        _log.Warn($"Live was still busy after {_profile.SettleTimeoutSeconds}s: '{LiveStatusText(mainWindow)}'", request.Job);
    }

    /// <summary>
    /// Leaves no half-open menu behind for the next attempt to trip over.
    ///
    /// Escape is aimed, in two steps. First it is <em>posted</em> to a Live window's own queue,
    /// which needs neither the foreground nor the screen and cannot reach anybody else's
    /// application; whether it worked is read back out of Live's window list. Only a menu that is
    /// still open earns a keystroke, and that one goes through the guard. Before this, the run
    /// injected a bare Escape at whatever happened to be in front - which, ten seconds and one user
    /// keystroke after Live was last focused, was not Live.
    /// </summary>
    private void CloseOpenMenus(Process process, IntPtr mainWindow, SeparationRequest request)
    {
        var foreground = WindowControl.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);

        // While a menu tracks, its popup - not Live's main window - is the foreground window and the
        // only thing that will answer the key. Aim at it when it is Live's; otherwise the main window.
        var target = foreground != IntPtr.Zero && foregroundPid == (uint)process.Id
            ? foreground
            : mainWindow;

        if (InputSimulator.PostKey(target, InputSimulator.VkEscape) && !LiveMenusOpen(process, mainWindow)) return;

        var escape = GuardedInput.PressKey(target, InputSimulator.VkEscape, "closed Live's menu");
        if (!escape.Injected)
            _log.Warn($"{escape.Sentence} - a half-open menu may be left for the next attempt", request.Job);
    }

    /// <summary>
    /// Whether Live still owns a popup window. A menu popup is its own top-level window and carries
    /// no title, which is also true of a few of Live's own panels, so this is a hint for where to
    /// send a key rather than a verdict on anything.
    /// </summary>
    private static bool LiveMenusOpen(Process process, IntPtr mainWindow) =>
        LiveProcessWindows.ForProcess(process.Id).Any(window =>
            window.Visible
            && window.Handle != mainWindow
            && (window.ClassName == "#32768" || string.IsNullOrWhiteSpace(window.Title)));

    /// <summary>
    /// Sets every stem toggle and the processing mode, verifying each before separating.
    /// Nothing is assumed: an unverifiable control stops the job.
    /// </summary>
    private void ConfigureDialog(IntPtr dialogWindow, SeparationRequest request)
    {
        var root = LiveUi.FromHandle(dialogWindow)
            ?? throw new LiveAutomationException(ErrorKind.LiveErrorDialog, "Could not read the Separate Stems dialog.");

        _log.Info("Separate Stems dialog tree:" + Environment.NewLine + LiveUi.DumpTree(root, 5, 200), request.Job);

        foreach (var stem in StemsExtensions.Order)
        {
            var wanted = request.Profile.Stems.HasFlag(stem);
            var control = LiveUi.FindByText(root, new[] { stem.LiveKeyword() })
                ?? LiveUi.FindByText(root, new[] { stem.LiveKeyword() }, restrictToInteractive: false)
                ?? throw new LiveAutomationException(ErrorKind.LiveErrorDialog,
                    $"Could not find the '{stem.DisplayName()}' toggle in the Separate Stems dialog.");

            if (!LiveUi.TrySetToggle(control, wanted))
                throw new LiveAutomationException(ErrorKind.LiveErrorDialog,
                    $"Could not set the '{stem.DisplayName()}' toggle.");

            if (LiveUi.ToggleState(control) != wanted)
                throw new LiveAutomationException(ErrorKind.LiveErrorDialog,
                    $"The '{stem.DisplayName()}' toggle did not reach the requested state.");

            _log.Info($"stem toggle {stem.DisplayName()} = {(wanted ? "on" : "off")} (verified)", request.Job);
        }

        var highQuality = LiveUi.FindByText(root, new[] { "high quality" })
            ?? LiveUi.FindByText(root, new[] { "high quality" }, restrictToInteractive: false)
            ?? throw new LiveAutomationException(ErrorKind.LiveErrorDialog,
                "Could not find the High Quality switch in the Separate Stems dialog.");

        var wantHighQuality = request.Profile.Mode.HighQualityToggle();
        if (!LiveUi.TrySetToggle(highQuality, wantHighQuality))
            throw new LiveAutomationException(ErrorKind.LiveErrorDialog, "Could not set the processing mode.");

        if (LiveUi.ToggleState(highQuality) != wantHighQuality)
            throw new LiveAutomationException(ErrorKind.LiveErrorDialog,
                $"The processing mode did not reach '{request.Profile.Mode.DisplayName()}'.");

        _log.Info($"processing mode {request.Profile.Mode.DisplayName()} (verified)", request.Job);

        var merge = LiveUi.FindByText(root, new[] { "merge to single track", "merge to single" });
        if (merge is not null)
        {
            LiveUi.TrySetToggle(merge, false);
            _log.Info("disabled 'Merge to Single Track'", request.Job);
        }
        else
        {
            _log.Info("this build's Separate Stems dialog has no 'Merge to Single Track' option", request.Job);
        }
    }

    private void ClickSeparate(Process process, IntPtr dialogWindow, SeparationRequest request)
    {
        var root = LiveUi.FromHandle(dialogWindow)
            ?? throw new LiveAutomationException(ErrorKind.LiveErrorDialog, "The Separate Stems dialog closed unexpectedly.");

        var button = LiveUi.FindByText(root, new[] { "separate" })
            ?? throw new LiveAutomationException(ErrorKind.LiveErrorDialog, "Could not find the Separate button.");

        if (!LiveUi.TryInvoke(button))
            throw new LiveAutomationException(ErrorKind.LiveErrorDialog, "Could not click the Separate button.");

        _log.Info("clicked Separate", request.Job);
    }

    // ------------------------------------------------------------------
    // Completion detection
    // ------------------------------------------------------------------

    /// <summary>
    /// Waits for Live to write the stems.
    ///
    /// Live closes the Separate Stems dialog the moment it starts rendering and writes the files
    /// only when the render is over, so a closed dialog and an empty folder are both the normal
    /// middle of a job. Measured here: a 4:53 track at High Speed ran with real-time factor 1.44,
    /// i.e. minutes of nothing on disk. The previous version read that silence as a cancellation,
    /// gave up after 1.7s, and the takeover that closed Live to start the next job is what Live's
    /// own log recorded as "Stem Separation ... cancelled".
    ///
    /// The batch is deliberately not interrupted by anything that looks like a prompt while it
    /// runs: Live's own conversion modal is the one window that must survive until the files land.
    /// </summary>
    private async Task WaitForSeparationAsync(
        Process process,
        IntPtr mainWindow,
        SeparationRequest request,
        IReadOnlyDictionary<string, FileState> baseline,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + request.Timeout;
        var startedAt = DateTime.UtcNow;
        var poll = TimeSpan.FromMilliseconds(700);
        var lastStates = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        var lastScratch = new Dictionary<string, StemFileMapper.FileStamp>(StringComparer.OrdinalIgnoreCase);
        var stableSince = (DateTime?)null;
        var lastLogged = DateTime.MinValue;
        var idleSince = (DateTime?)null;

        while (DateTime.UtcNow < deadline)
        {
            CheckAbort(process, request, interacting: false);

            var current = SnapshotStems(request);
            var now = DateTime.UtcNow;
            var scratch = ScratchStamps(current);

            var render = StemFileMapper.ClassifyRender(
                NewOrChangedFiles(current, baseline), lastScratch, scratch, RenderStallPatience, now);

            // Every target the next round compares itself against, whether or not this round found
            // any audio. Assigning it only on the audio path is what made the plateau invisible.
            lastScratch = scratch;

            // Only a committed file counts as output. Live's render targets carry no extension at
            // all, and treating them as stems is what let a half-written render be reported as a
            // finished one that then collected nothing.
            var fresh = render.Committed;

            // Live renames its render targets when the last stem is written, so audio that lands
            // while one of them is still being written belongs to a render in progress. "Still being
            // written" now means exactly that - the target advanced - so a leftover from a crashed
            // run holds neither this gate nor the silence clock below, and a plateau of its own is
            // named instead of waited out.
            var committing = render.Committing;

            if (fresh.Count > 0)
            {
                var unchanged = !committing && fresh.All(path =>
                    lastStates.TryGetValue(path, out var previous) && previous == current[path]);

                if (unchanged)
                {
                    stableSince ??= DateTime.UtcNow;
                    if ((DateTime.UtcNow - stableSince.Value).TotalSeconds >= _profile.FileStabilitySeconds)
                    {
                        if (render.Stalled.Count > 0)
                            _log.Warn($"Live committed {fresh.Count} file(s) and left " +
                                      $"{render.Stalled.Count} render target(s) unfinished: {NameTargets(render.Stalled, current)}",
                                      request.Job);

                        _log.Info($"{fresh.Count} generated file(s) have been stable for {_profile.FileStabilitySeconds:F1}s", request.Job);
                        return;
                    }
                }
                else
                {
                    stableSince = null;
                }

                lastStates = current;
            }
            else
            {
                // Live is still working while it says so or while it still holds a window - the
                // conversion runs in a modal loop. Only a Live that has been visibly quiet for
                // longer than a render needs to get going is a Live that refused.
                var working = committing
                              || IsLiveBusy(LiveStatusText(mainWindow))
                              || LiveProcessWindows.OtherVisibleWindows(process.Id, mainWindow).Count > 0;

                idleSince = working ? null : idleSince ?? DateTime.UtcNow;

                if (idleSince is not null
                    && idleSince.Value - startedAt > RenderStartGrace
                    && DateTime.UtcNow - idleSince.Value > SilenceIsACancellation)
                {
                    var stalled = render.Stalled.Count > 0;
                    CaptureDiagnostic(process, request, stalled ? "separation-stalled" : "separation-silent");

                    if (stalled)
                    {
                        // The useful sentence is not "it timed out" but "it stopped here, at this
                        // size, and this is the file it left behind". Named, sized and dated, because
                        // the next question is whether Live is stuck or the render was abandoned.
                        throw new LiveAutomationException(ErrorKind.SeparationTimeout,
                            $"Ableton Live stopped the render part-written and committed nothing: " +
                            $"{NameTargets(render.Stalled, current)} had not advanced for " +
                            $"{(DateTime.UtcNow - idleSince.Value).TotalSeconds:F0}s while Live went quiet. " +
                            "These are Live's own unfinished render targets, not stems - close and " +
                            "reopen the set, or separate the track again.");
                    }

                    throw new LiveAutomationException(ErrorKind.SeparationCancelled,
                        "Ableton Live went quiet without writing a single stem file - the separation " +
                        "was refused or cancelled inside Live.");
                }

                if ((DateTime.UtcNow - lastLogged).TotalSeconds >= 20)
                {
                    _log.Info($"separating: {(DateTime.UtcNow - startedAt).TotalSeconds:F0}s elapsed, " +
                              $"no stem files yet (Live says '{LiveStatusText(mainWindow)}'" +
                              (render.Writing.Count > 0 ? $", {render.Writing.Count} render target(s) still growing" : "") +
                              (render.Stalled.Count > 0 ? $", {render.Stalled.Count} render target(s) plateaued" : "") +
                              ")", request.Job);
                    lastLogged = DateTime.UtcNow;
                }
            }

            await Task.Delay(poll, ct).ConfigureAwait(false);
        }

        throw new LiveAutomationException(ErrorKind.SeparationTimeout,
            $"Stem separation did not finish within {request.Timeout.TotalMinutes:F0} minutes" +
            (lastScratch.Count > 0
                ? $" and left its render targets behind: {NameTargets(lastScratch.Keys.ToList(), lastStates)}"
                : ".") );
    }

    /// <summary>How long a render target may stop advancing before that means something.</summary>
    /// <remarks>
    /// Measured here: Live's targets sat size-stable for about two seconds in the middle of a render
    /// that then finished. Anything under that would call a working render stalled, and a plateau
    /// this side of a minute is more likely a slow disk than a dead render - so the patience is long
    /// and the stall is only acted on when Live has also gone quiet.
    /// </remarks>
    private static readonly TimeSpan RenderStallPatience = TimeSpan.FromSeconds(20);

    /// <summary>The render targets in a snapshot, as the mapper compares them poll to poll.</summary>
    private static Dictionary<string, StemFileMapper.FileStamp> ScratchStamps(
        Dictionary<string, FileState> snapshot)
    {
        var stamps = new Dictionary<string, StemFileMapper.FileStamp>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, state) in snapshot)
        {
            if (StemFileMapper.IsLiveScratchFile(path))
                stamps[path] = new StemFileMapper.FileStamp(state.Size, state.Write);
        }

        return stamps;
    }

    /// <summary>
    /// Live's render targets, spelled out: file name, bytes and how long ago each was touched. A
    /// folder full of <c>AbletonTmp</c> files is the evidence that a render stopped, and a job that
    /// says only "timed out" throws that evidence away.
    /// </summary>
    private static string NameTargets(IReadOnlyList<string> targets, IReadOnlyDictionary<string, FileState> snapshot)
    {
        var now = DateTime.UtcNow;

        return string.Join(", ", targets.Take(6).Select(path =>
            snapshot.TryGetValue(path, out var state)
                ? StemFileMapper.DescribeRenderTarget(path, state.Size, state.Write, now)
                : StemFileMapper.RenderTargetName(path)));
    }

    /// <summary>How long a render is allowed to look idle before that means anything.</summary>
    private static readonly TimeSpan RenderStartGrace = TimeSpan.FromSeconds(30);

    /// <summary>Idle for this long with no files: Live is not going to produce anything.</summary>
    private static readonly TimeSpan SilenceIsACancellation = TimeSpan.FromSeconds(60);

    // ------------------------------------------------------------------
    // Output collection
    // ------------------------------------------------------------------

    private IReadOnlyDictionary<Stems, string> CollectGeneratedStems(SeparationRequest request)
    {
        var candidates = StemCandidateFolders(request)
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(StemFileMapper.LooksLikeStemFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        var mapped = StemFileMapper.Map(request.Profile.Stems, candidates);
        var final = new Dictionary<Stems, string>();

        foreach (var (stem, path) in mapped)
        {
            try
            {
                var converted = StemFormatConverter.EnsureWav(path);
                if (converted.Converted)
                    _log.Info($"converted {Path.GetFileName(path)} to WAV (no processing applied)", request.Job);
                final[stem] = converted.Path;
            }
            catch (Exception ex)
            {
                _log.Warn($"could not prepare the {stem.DisplayName()} stem: {ex.Message}", request.Job);
            }
        }

        foreach (var stem in request.Profile.Stems.Enumerate())
        {
            _log.Info(final.ContainsKey(stem)
                ? $"{stem.DisplayName()} -> {Path.GetFileName(final[stem])}"
                : $"{stem.DisplayName()} -> not produced", request.Job);
        }

        return final;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Dictionary<string, FileState> SnapshotFolder(string folder)
    {
        var result = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return result;

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                result[file] = new FileState(info.Length, info.LastWriteTimeUtc);
            }
            catch
            {
                // A file that vanished between enumerate and stat is simply ignored.
            }
        }

        return result;
    }

    /// <summary>
    /// Folders Live may have written the stems into.
    ///
    /// The working set is built as a real Live project, so the expected answer is one folder.
    /// It is still not the only one watched, because a set Live decides is loose gets copied into
    /// "Documents\Ableton\Live Recordings\<stamp> Temp Project" and the stems are generated there
    /// - measured with Live 12.3.5, where the separation reported success and autostem saw an
    /// empty folder. Watching both costs nothing: the baseline is taken the same way as the poll,
    /// so a temp project left by an earlier run can never be mistaken for this job's output.
    /// </summary>
    private IReadOnlyList<string> StemCandidateFolders(SeparationRequest request)
    {
        try
        {
            return LiveStemFolders.Candidates(
                request.LiveStemsFolder,
                LiveStemFolders.RecordingsRoot(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                TempProjectsToWatch);
        }
        catch (Exception ex)
        {
            _log.Warn($"Live's temporary projects could not be listed: {ex.Message}", request.Job);
            return new[] { request.LiveStemsFolder };
        }
    }

    private Dictionary<string, FileState> SnapshotStems(SeparationRequest request)
    {
        var all = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in StemCandidateFolders(request))
            foreach (var (path, state) in SnapshotFolder(folder))
                all[path] = state;
        return all;
    }

    private static List<string> NewOrChangedFiles(
        Dictionary<string, FileState> current,
        IReadOnlyDictionary<string, FileState> baseline)
    {
        var result = new List<string>();
        foreach (var (path, state) in current)
        {
            if (!baseline.TryGetValue(path, out var previous) || previous != state)
                result.Add(path);
        }
        return result;
    }

    private void Report(SeparationRequest request, JobStage stage, string? detail = null, double? percent = null)
    {
        _log.Stage(request.Job, stage, detail);
        request.Progress?.Report(new SeparationProgress(stage, detail, percent));
    }

    /// <summary>How many times Live is brought back before a stolen focus counts as real.</summary>
    private const int FocusRecoveryAttempts = 5;

    /// <summary>
    /// Stops the run for a reason a person can act on.
    ///
    /// <paramref name="interacting"/> says whether this loop is about to send input, and it
    /// decides two things a watching loop must not insist on: a desktop that can receive clicks,
    /// and Live holding the foreground. A render that is already running needs neither, and
    /// stopping one because the user locked the screen and walked away was itself a defect.
    /// </summary>
    private void CheckAbort(Process process, SeparationRequest request, bool interacting)
    {
        if (interacting) DesktopGuard.Require(" while answering a Live prompt");

        if (!LiveProcessWindows.IsAlive(process.Id))
            throw new LiveAutomationException(ErrorKind.LiveCrashed, "Ableton Live exited unexpectedly.");

        var foreground = WindowControl.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);

        var isOurs = foregroundPid == (uint)_ownProcessId;
        var isLive = foregroundPid == (uint)process.Id;

        if (isOurs || isLive) return;
        if (!interacting) return;

        // A utility that flashes a window - a display dimmer, a volume overlay, a mail toast -
        // is not a reason to stop a batch that runs for an hour, and stopping on the first
        // glimpse of one did exactly that here. Live is brought back and the question is asked
        // again; only a foreground that survives every attempt is a real takeover.
        var thief = WindowControl.GetTitle(foreground);

        for (var attempt = 1; attempt <= FocusRecoveryAttempts; attempt++)
        {
            Thread.Sleep(400);

            var main = LiveProcessWindows.MainWindow(process.Id);
            if (main is not null) WindowControl.Activate(main.Handle);

            NativeMethods.GetWindowThreadProcessId(WindowControl.GetForegroundWindow(), out var nowPid);
            if (nowPid == (uint)process.Id || nowPid == (uint)_ownProcessId)
            {
                _log.Info($"took the focus back from '{thief}' on attempt {attempt}", request.Job);
                return;
            }
        }

        throw new LiveAutomationException(ErrorKind.LiveLostFocus,
            $"Ableton Live lost the foreground to '{thief}' and would not take it back after " +
            $"{FocusRecoveryAttempts} attempts. Close that window and resume the batch.");
    }

    private void FocusLive(IntPtr mainWindow)
    {
        if (!WindowControl.TryActivate(mainWindow))
            throw new LiveAutomationException(ErrorKind.LiveLostFocus, "Could not focus the Ableton Live window.");
        Thread.Sleep(200);
    }

    /// <summary>
    /// Gives the screen back for the length of a render.
    ///
    /// Live will not accept the commands that set a job up unless it is the foreground window -
    /// it ignores posted *mouse* messages, so those few clicks have to be real ones, and only its
    /// dialogs answer a posted key. It needs no input whatever while it renders, though, and
    /// rendering is the part that takes minutes. Everything after the Separate click is a UIA read
    /// and a folder scan, both of which work on a window that is behind everything else, so the
    /// screen belongs to the user until the next track.
    ///
    /// Best effort by design: a window that has since been closed, minimised or taken over is
    /// left exactly where the user put it, and nothing about the job depends on this succeeding.
    /// </summary>
    private void YieldScreen(IntPtr screenOwner, Process process, SeparationRequest request)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(screenOwner, out var ownerPid);

            var foreground = WindowControl.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);

            var plan = ScreenYield.Plan(new ScreenYieldFacts(
                OwnerExists: screenOwner != IntPtr.Zero && NativeMethods.IsWindow(screenOwner),
                OwnerIsVisible: NativeMethods.IsWindowVisible(screenOwner),
                OwnerIsLive: ownerPid == (uint)process.Id,
                OwnerIsForeground: foreground == screenOwner,
                ForegroundIsOursOrLive: foregroundPid == (uint)process.Id || foregroundPid == (uint)_ownProcessId));

            if (plan != ScreenYieldPlan.HandBack)
            {
                _log.Info($"leaving the screen as it is ({plan})", request.Job);
                return;
            }

            if (WindowControl.Activate(screenOwner))
                _log.Info($"the render is left to itself; the screen is back with '{WindowControl.GetTitle(screenOwner)}'",
                    request.Job);
        }
        catch (Exception ex)
        {
            _log.Info($"the screen was left as it is ({ex.Message})", request.Job);
        }
    }

    private void CaptureDiagnostic(Process? process, SeparationRequest request, string reason)
    {
        if (!_profile.CaptureDiagnosticScreenshots) return;

        try
        {
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{reason}.png";

            if (process is not null)
            {
                var main = LiveProcessWindows.MainWindow(process.Id);
                if (main is not null)
                {
                    var path = ScreenCapture.TrySaveWindow(main.Handle, _diagnosticsFolder, name);
                    if (path is not null)
                    {
                        _log.Info($"diagnostic screenshot: {path}", request.Job);
                        return;
                    }
                }
            }

            using var desktop = ScreenCapture.CaptureDesktop();
            _log.Info($"diagnostic screenshot: {ScreenCapture.Save(desktop, _diagnosticsFolder, name)}", request.Job);
        }
        catch
        {
            // Diagnostics are best effort and must never mask the real failure.
        }
    }

    /// <summary>
    /// Closing a working session uses the same path as taking one over, because the prompts Live
    /// raises on the way out are the same prompts - and both need the same bounded, never-stuck
    /// answer.
    /// </summary>
    private async Task CloseLiveAsync(Process process, CancellationToken ct)
    {
        await LiveSessionTakeover.CloseAsync(process, _log, null, ct).ConfigureAwait(false);
    }
}

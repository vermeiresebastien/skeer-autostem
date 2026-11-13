using System.Diagnostics;
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
///   * Live is only ever given the working set this tool generated; an unrelated set is a
///     stop condition, never something to close or modify.
///   * Input is only sent after the Live window, foreground state and desktop are verified.
///   * Every wait is bounded and every stage reports progress.
///   * A file-specific failure is skipped only after Live is back in a known state; an
///     unknown state pauses the batch instead of attempting blind recovery.
/// </summary>
public sealed class LiveAutomationRunner : ILiveRunner
{
    private sealed record FileState(long Size, DateTime Write);

    private readonly RunLog _log;
    private readonly LiveUiProfile _profile;
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly string _diagnosticsFolder = Path.Combine(AppPaths.LogsFolder, "screenshots");

    public string Name => "Ableton Live (automation)";

    public LiveAutomationRunner(RunLog log, LiveUiProfile? profile = null)
    {
        _log = log;
        _profile = profile ?? LiveUiProfile.Load();
    }

    public async Task<SeparationOutcome> SeparateAsync(SeparationRequest request, CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            Report(request, JobStage.CheckingSource);

            if (WindowControl.IsDesktopLocked())
                throw new LiveAutomationException(ErrorKind.DesktopLocked,
                    "Windows is locked. Unlock the desktop and resume the batch.");

            _log.Info($"Live install: {request.Install.DisplayName} ({request.Install.ExePath})", request.Job);

            if (!request.Install.SupportsStemSeparation)
                throw new LiveAutomationException(ErrorKind.LiveVersionUnsupported,
                    $"Ableton Live {request.Install.Version} has no stem separation. Live 12.1 or newer is required.");

            if (!File.Exists(request.BaseTemplatePath))
                throw new LiveAutomationException(ErrorKind.LiveVersionUnsupported,
                    "Live's audio-track template could not be found, so the working project cannot be built.");

            await EnsureNoForeignLiveSessionAsync(request, cancellationToken).ConfigureAwait(false);

            Report(request, JobStage.PreparingProject);
            var spec = new AudioClipSpec(
                request.Source.Path,
                request.Source.SizeBytes,
                request.SourceInfo.SampleFrames,
                request.SourceInfo.SampleRate,
                Path.GetFileNameWithoutExtension(request.Source.Path));

            var projectPath = WorkingProjectBuilder.CreateProject(request.WorkingFolder, request.BaseTemplatePath, spec);
            _log.Info($"working project: {projectPath}", request.Job);

            var baseline = SnapshotFolder(request.LiveStemsFolder);
            _log.Info($"stem baseline: {baseline.Count} pre-existing file(s)", request.Job);

            Report(request, JobStage.LaunchingLive);
            process = LaunchLive(request.Install.ExePath, projectPath);
            _log.Info($"launched Live (pid {process.Id})", request.Job);

            Report(request, JobStage.WaitingForLive);
            var mainWindow = await WaitForMainWindowAsync(process, request, cancellationToken).ConfigureAwait(false);

            Report(request, JobStage.OpeningProject);
            await WaitForSetLoadedAsync(process, mainWindow, request, cancellationToken).ConfigureAwait(false);

            Report(request, JobStage.SelectingClip);
            SelectClip(mainWindow, spec, request);

            Report(request, JobStage.OpeningStemDialog);
            var dialog = await OpenSeparateStemsDialogAsync(process, mainWindow, request, cancellationToken).ConfigureAwait(false);

            Report(request, JobStage.ConfiguringStemDialog);
            ConfigureDialog(dialog, request);

            Report(request, JobStage.Separating);
            var stopwatch = Stopwatch.StartNew();
            ClickSeparate(process, dialog, request);
            await WaitForSeparationAsync(process, dialog, request, baseline, cancellationToken).ConfigureAwait(false);
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
            process?.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Preflight: never interfere with a user's own Live session
    // ------------------------------------------------------------------

    private async Task EnsureNoForeignLiveSessionAsync(SeparationRequest request, CancellationToken ct)
    {
        var running = LiveProcessWindows.FindLiveProcesses();
        if (running.Count == 0) return;

        try
        {
            foreach (var proc in running)
            {
                var title = WindowControl.GetMainWindowTitle(proc);
                _log.Info($"Live already running (pid {proc.Id}, title '{title}')", request.Job);

                if (!title.Contains("autoSTEM", StringComparison.OrdinalIgnoreCase))
                {
                    throw new LiveAutomationException(ErrorKind.LiveAlreadyRunningWithUserProject,
                        "Ableton Live is already open with what looks like your own project. " +
                        "Save and close it, then resume the batch - Skeer autoSTEM will not touch it.");
                }
            }

            // It is a leftover working set from a previous run: close it so this job starts clean.
            _log.Warn("closing a leftover Skeer autoSTEM working set before starting a new job", request.Job);
            foreach (var proc in running)
            {
                await CloseLiveAsync(proc, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var proc in running) proc.Dispose();
        }
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

        while (DateTime.UtcNow < deadline)
        {
            CheckAbort(process, request, tolerateForeignForeground: true);

            var main = LiveProcessWindows.MainWindow(process.Id);
            if (main is not null)
            {
                handle = main.Handle;
                break;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        if (handle == IntPtr.Zero)
            throw new LiveAutomationException(ErrorKind.LiveWindowNotFound, "Ableton Live did not open a window in time.");

        if (!WindowControl.Activate(handle))
            throw new LiveAutomationException(ErrorKind.LiveLostFocus, "Could not focus the Ableton Live window.");

        WindowControl.Maximize(handle);
        await Task.Delay(500, ct).ConfigureAwait(false);
        return handle;
    }

    private async Task WaitForSetLoadedAsync(Process process, IntPtr mainWindow, SeparationRequest request, CancellationToken ct)
    {
        var projectName = Path.GetFileNameWithoutExtension(request.ProjectFilePath);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(_profile.SetLoadTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            CheckAbort(process, request, tolerateForeignForeground: false);
            await HandleUnexpectedDialogsAsync(process, mainWindow, request, ct).ConfigureAwait(false);

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
    /// Live shows a recovery prompt after an unclean shutdown and may ask about missing
    /// files. Only safe, never-destructive answers are chosen; anything else pauses the batch.
    /// </summary>
    private async Task HandleUnexpectedDialogsAsync(Process process, IntPtr mainWindow, SeparationRequest request, CancellationToken ct)
    {
        var dialog = LiveProcessWindows.OtherVisibleWindows(process.Id, mainWindow).FirstOrDefault();
        if (dialog is null) return;

        if (IsExpectedWindowTitle(dialog.Title))
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
            return;
        }

        _log.Warn($"unexpected Live dialog '{dialog.Title}'", request.Job);
        CaptureDiagnostic(process, request, "unexpected-dialog");

        var root = LiveUi.FromHandle(dialog.Handle);
        var safe = root is null
            ? null
            : LiveUi.FindByText(root, new[] { "don't recover", "dont recover", "discard", "don't save", "cancel", "no" });

        if (safe is not null && LiveUi.TryInvoke(safe))
        {
            _log.Warn($"dismissed dialog '{dialog.Title}' with a safe default", request.Job);
            await Task.Delay(500, ct).ConfigureAwait(false);
            return;
        }

        throw new LiveAutomationException(ErrorKind.UnexpectedDialog,
            $"Ableton Live showed an unexpected dialog ('{dialog.Title}') that Skeer autoSTEM will not answer automatically. " +
            "Close it, then resume the batch.");
    }

    private static bool IsExpectedWindowTitle(string title) =>
        title.Contains("Separate", StringComparison.OrdinalIgnoreCase)
        || title.Contains("Stem", StringComparison.OrdinalIgnoreCase)
        || title.Contains("Progress", StringComparison.OrdinalIgnoreCase)
        || title.Contains("autoSTEM", StringComparison.OrdinalIgnoreCase)
        || title.Contains("Ableton Live", StringComparison.OrdinalIgnoreCase);

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
            // "<clip name>, stopped, in track <track>, scene <n>".
            var clip = LiveUi.WaitForText(root, new[] { spec.ClipName }, TimeSpan.FromSeconds(5));
            if (clip is not null && LiveUi.TrySelect(clip))
            {
                _log.Info($"selected clip '{LiveUi.Name(clip)}' through UI Automation", request.Job);
                Thread.Sleep(300);
                return;
            }

            _log.Warn($"the clip slot for '{spec.ClipName}' was not found through UI Automation", request.Job);
        }

        var rect = WindowControl.GetRect(mainWindow)
            ?? throw new LiveAutomationException(ErrorKind.LiveWindowNotFound, "The Live window disappeared.");

        var x = rect.X + (int)(rect.Width * _profile.ClipXFraction);
        var y = rect.Y + (int)(rect.Height * _profile.ClipYFraction);

        _log.Info($"selecting clip with a window-relative click at {x},{y}", request.Job);
        InputSimulator.LeftClick(x, y);
        Thread.Sleep(350);
    }

    private async Task<IntPtr> OpenSeparateStemsDialogAsync(Process process, IntPtr mainWindow, SeparationRequest request, CancellationToken ct)
    {
        FocusLive(mainWindow);

        var root = LiveUi.FromHandle(mainWindow);
        if (root is not null && LiveUi.TryOpenMenu(root, "Create"))
        {
            var item = LiveUi.WaitForMenuItem(process.Id, "separate stems", TimeSpan.FromSeconds(8));
            if (item is not null && LiveUi.TrySelect(item))
            {
                _log.Info("opened Separate Stems through the UI Automation menu", request.Job);
                return await WaitForStemDialogAsync(process, mainWindow, request, ct).ConfigureAwait(false);
            }

            _log.Warn("the Create menu opened but no Separate Stems entry was found", request.Job);
            InputSimulator.KeyPress(InputSimulator.VkEscape);
        }

        // Fallback: open the Create menu with a window-relative click, then choose the item.
        var rect = WindowControl.GetRect(mainWindow)
            ?? throw new LiveAutomationException(ErrorKind.LiveWindowNotFound, "The Live window disappeared.");

        var menuX = rect.X + (int)(rect.Width * _profile.CreateMenuXFraction);
        var menuY = rect.Y + (int)(rect.Height * _profile.MenuBarYFraction);
        _log.Info($"opening the Create menu with a window-relative click at {menuX},{menuY}", request.Job);
        InputSimulator.LeftClick(menuX, menuY);
        await Task.Delay(450, ct).ConfigureAwait(false);

        var clicked = LiveUi.WaitForMenuItem(process.Id, "separate stems", TimeSpan.FromSeconds(5));
        if (clicked is not null && LiveUi.TrySelect(clicked))
        {
            _log.Info("chose the Separate Stems menu item after a coordinate click", request.Job);
            return await WaitForStemDialogAsync(process, mainWindow, request, ct).ConfigureAwait(false);
        }

        CaptureDiagnostic(process, request, "create-menu");
        throw new LiveAutomationException(ErrorKind.LiveErrorDialog,
            "Could not find 'Separate Stems to New Audio Tracks' in Live's Create menu.");
    }

    private async Task<IntPtr> WaitForStemDialogAsync(Process process, IntPtr mainWindow, SeparationRequest request, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(_profile.DialogTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var dialog = LiveProcessWindows.OtherVisibleWindows(process.Id, mainWindow).FirstOrDefault();
            if (dialog is not null)
            {
                _log.Info($"Separate Stems dialog window: '{dialog.Title}'", request.Job);
                await Task.Delay(500, ct).ConfigureAwait(false);
                return dialog.Handle;
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        throw new LiveAutomationException(ErrorKind.LiveErrorDialog,
            "The Separate Stems dialog did not appear. The command may be unavailable for this clip.");
    }

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
    /// Bounded completion detection. Separation counts as finished when Live has produced
    /// files and stopped writing to them, or when the dialog closes. Cancellation and
    /// failure are distinguished from success rather than being treated as a timeout.
    /// </summary>
    private async Task WaitForSeparationAsync(
        Process process,
        IntPtr dialogWindow,
        SeparationRequest request,
        IReadOnlyDictionary<string, FileState> baseline,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + request.Timeout;
        var poll = TimeSpan.FromMilliseconds(700);
        var lastStates = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
        var stableSince = (DateTime?)null;
        var sawDialog = true;

        while (DateTime.UtcNow < deadline)
        {
            CheckAbort(process, request, tolerateForeignForeground: true);

            var current = SnapshotFolder(request.LiveStemsFolder);
            var fresh = NewOrChangedFiles(current, baseline);

            if (fresh.Count > 0)
            {
                var unchanged = fresh.All(path =>
                    lastStates.TryGetValue(path, out var previous) && previous == current[path]);

                if (unchanged)
                {
                    stableSince ??= DateTime.UtcNow;
                    if ((DateTime.UtcNow - stableSince.Value).TotalSeconds >= _profile.FileStabilitySeconds)
                    {
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
                // No output yet: if the dialog has gone away, Live did not produce stems.
                var dialogVisible = NativeMethods.IsWindow(dialogWindow) && NativeMethods.IsWindowVisible(dialogWindow);

                if (sawDialog && !dialogVisible)
                {
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                    var afterClose = SnapshotFolder(request.LiveStemsFolder);
                    if (NewOrChangedFiles(afterClose, baseline).Count == 0)
                    {
                        throw new LiveAutomationException(ErrorKind.SeparationCancelled,
                            "The Separate Stems dialog closed without producing any stems - the separation was cancelled or failed in Live.");
                    }
                    return;
                }
            }

            await Task.Delay(poll, ct).ConfigureAwait(false);
        }

        throw new LiveAutomationException(ErrorKind.SeparationTimeout,
            $"Stem separation did not finish within {request.Timeout.TotalMinutes:F0} minutes.");
    }

    // ------------------------------------------------------------------
    // Output collection
    // ------------------------------------------------------------------

    private IReadOnlyDictionary<Stems, string> CollectGeneratedStems(SeparationRequest request)
    {
        var folder = request.LiveStemsFolder;
        if (!Directory.Exists(folder)) return new Dictionary<Stems, string>();

        var candidates = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(StemFileMapper.LooksLikeStemFile)
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

    /// <summary>
    /// Verifies automation may still send input. Anything unexpected stops with a clear
    /// reason instead of continuing blindly.
    /// </summary>
    private void CheckAbort(Process process, SeparationRequest request, bool tolerateForeignForeground)
    {
        if (WindowControl.IsDesktopLocked())
            throw new LiveAutomationException(ErrorKind.DesktopLocked, "Windows was locked during the run; automation stopped.");

        if (!LiveProcessWindows.IsAlive(process.Id))
            throw new LiveAutomationException(ErrorKind.LiveCrashed, "Ableton Live exited unexpectedly.");

        var foreground = WindowControl.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundPid);

        var isOurs = foregroundPid == (uint)_ownProcessId;
        var isLive = foregroundPid == (uint)process.Id;

        if (isOurs || isLive) return;
        if (tolerateForeignForeground) return;

        throw new LiveAutomationException(ErrorKind.LiveLostFocus,
            "Ableton Live lost the foreground because another window took focus. Automation stopped.");
    }

    private void FocusLive(IntPtr mainWindow)
    {
        if (!WindowControl.Activate(mainWindow))
            throw new LiveAutomationException(ErrorKind.LiveLostFocus, "Could not focus the Ableton Live window.");
        Thread.Sleep(200);
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

    private async Task CloseLiveAsync(Process process, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var main = LiveProcessWindows.MainWindow(process.Id);
            if (main is not null)
                CloseRequest.CloseWindow(main.Handle);

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline && LiveProcessWindows.IsAlive(process.Id))
                await Task.Delay(300, ct).ConfigureAwait(false);

            if (LiveProcessWindows.IsAlive(process.Id))
            {
                _log.Warn("Live did not close gracefully; terminating the working session");
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"could not close Live cleanly: {ex.Message}");
        }
    }
}

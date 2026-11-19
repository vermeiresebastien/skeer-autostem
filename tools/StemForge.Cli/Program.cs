using StemForge.Core.Audio;
using StemForge.Core.Engines;
using StemForge.Core.Live;
using StemForge.Core.Live.Projects;
using StemForge.Core.Logging;
using StemForge.Core.Models;
using StemForge.Core.Orchestration;
using StemForge.Core.Queue;
using StemForge.Core.Support;
using StemForge.Core.Verify;

namespace StemForge.Cli;

/// <summary>
/// Headless companion to the desktop app. It drives exactly the same Core code, which
/// makes it useful for overnight batches, for CI, and for reproducing a run when
/// something goes wrong in the GUI.
///
///   stemforge locate
///   stemforge prepare &lt;source&gt; [--work DIR]
///   stemforge run &lt;source...&gt; --dest DIR [--stems all|drums|vocals,drums] [--mode speed|quality] [--simulate]
///   stemforge verify &lt;resultFolder&gt;
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0) { Usage(); return 1; }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "locate" => Locate(),
                "prepare" => Prepare(args.Skip(1).ToArray()),
                "run" => await RunAsync(args.Skip(1).ToArray()),
                "verify" => Verify(args.Skip(1).ToArray()),
                "enqueue" => Enqueue(args.Skip(1).ToArray()),
                "dump-ui" => DumpUi(args.Skip(1).ToArray()),
                "uia-invoke" => UiaInvoke(args.Skip(1).ToArray()),
                "click-probe" => ClickProbe(args.Skip(1).ToArray()),
                "bench" => await BenchAsync(args.Skip(1).ToArray()),
                "-h" or "--help" or "help" => Usage(),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            autostem - batch stem separation for Ableton Live (Skeer autoSTEM companion)

              locate                                  list detected Ableton Live installations
              prepare <source> [--work DIR]           build the working Live Set for a file and print its path
              enqueue <path...> --dest DIR            add files or folders to the saved queue without running
                              [--stems all|drums|vocals,drums] [--mode speed|quality]
              run <source...> --dest DIR [options]    separate files (options: --stems, --mode,
                                                      --simulate, --engine live, --work)
              verify <resultFolder>                   re-verify a published result folder
              bench <short> <longer> --dest DIR [--simulate]
                                                      compare All vs Drums Only in both modes
              dump-ui [--window TEXT] [--depth N] [--open-menu NAME]
                                                      dump UI Automation tree of a window
              uia-invoke --button TEXT [--window TEXT]
                                                      press a button through UI Automation
              click-probe --name TEXT [--window TEXT] [--mode post|send|real|invoke] [--activate] [--close]
                                                      press one element and print the menus it owns
                                                      before and after, to show whether it worked
            """);
        return 0;
    }

    private static int Locate()
    {
        var installs = LiveLocator.FindAll();
        if (installs.Count == 0)
        {
            Console.WriteLine("No Ableton Live installation detected.");
            return 1;
        }

        foreach (var install in installs)
        {
            Console.WriteLine($"{install.DisplayName}");
            Console.WriteLine($"    exe              : {install.ExePath}");
            // The same verdict the Run button and the engine give - version AND edition - so
            // `locate` cannot call a build usable that everything else refuses. The reason token
            // is printed so a report can be read without guessing which half failed.
            var support = install.Support;
            Console.WriteLine(support.IsSupported
                ? $"    stem separation  : yes ({support.Reason})"
                : $"    stem separation  : no ({support.Reason}) - {support.Sentence}");
            Console.WriteLine($"    template         : {WorkingProjectBuilder.FindBaseTemplate(install) ?? "<not found>"}");
        }

        return 0;
    }

    private static int Prepare(string[] args)
    {
        var source = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (source is null || !File.Exists(source)) { Console.Error.WriteLine("error: source file not found"); return 1; }

        var work = Option(args, "--work") ?? Path.Combine(
            Path.GetTempPath(), "SkeerAutoSTEMWork", Path.GetFileNameWithoutExtension(source));

        var install = LiveLocator.Locate();
        if (install is null) { Console.Error.WriteLine("error: Ableton Live was not found"); return 1; }

        var template = WorkingProjectBuilder.FindBaseTemplate(install);
        if (template is null) { Console.Error.WriteLine("error: Live's audio track template was not found"); return 1; }

        var info = AudioFileReader.ReadInfo(source);
        var spec = new AudioClipSpec(
            Path.GetFullPath(source), new FileInfo(source).Length, info.SampleFrames, info.SampleRate,
            Path.GetFileNameWithoutExtension(source));

        var project = WorkingProjectBuilder.CreateProject(work, template, spec);

        Console.WriteLine($"source   : {Path.GetFullPath(source)} ({info})");
        Console.WriteLine($"template : {template}");
        Console.WriteLine($"project  : {project}");
        Console.WriteLine($"stems to : {Path.Combine(work, "Samples", "Processed", "Stems")}");
        return 0;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var destination = Option(args, "--dest");
        if (string.IsNullOrWhiteSpace(destination))
        {
            Console.Error.WriteLine("error: --dest is required");
            return 1;
        }

        var sources = args.Where(a => !a.StartsWith("--") && a != destination).ToArray();
        if (sources.Length == 0) { Console.Error.WriteLine("error: no source files given"); return 1; }

        var simulate = args.Contains("--simulate");
        var stems = ParseStems(Option(args, "--stems") ?? "all");
        var mode = (Option(args, "--mode") ?? "speed").StartsWith("qual", StringComparison.OrdinalIgnoreCase)
            ? ProcessingMode.HighQuality
            : ProcessingMode.HighSpeed;

        var profile = new JobProfile(stems, mode);

        using var log = new RunLog();
        var settings = new SettingsStore().Load();
        settings.UseSimulatedRunner = simulate;
        settings.KeepAwakeDuringRun = !simulate;

        var queue = new QueueService(new QueueStore());
        queue.Load();
        queue.AddPaths(sources, recursive: false, profile, Path.GetFullPath(destination));

        string template = string.Empty;
        LiveInstall install;
        if (simulate)
        {
            install = new LiveInstall(string.Empty, "0.0", LiveInstall.SimulatedEdition);
        }
        else
        {
            install = LiveLocator.Locate() ?? throw new InvalidOperationException("Ableton Live was not found.");
            template = WorkingProjectBuilder.FindBaseTemplate(install)
                ?? throw new InvalidOperationException("Live's audio track template was not found.");
            Console.WriteLine($"engine : {install.DisplayName} ({install.ExePath})");
        }

        Console.WriteLine($"queue  : {queue.Jobs.Count} job(s), {profile.Descriptor} -> {Path.GetFullPath(destination)}");
        Console.WriteLine($"log    : {log.FilePath}");
        Console.WriteLine();

        var engine = Engine(settings, log, args);

        var runner = new BatchRunner(engine);
        var context = new BatchContext
        {
            Settings = settings,
            Install = install,
            BaseTemplatePath = template,
            Log = log,
            PersistQueue = queue.Save,
            Progress = new Progress<BatchProgress>(p =>
                Console.WriteLine($"  [{p.Stage.DisplayName()}] {p.TrackName} {(string.IsNullOrEmpty(p.Detail) ? string.Empty : "- " + p.Detail)}")),
        };

        using var guard = new LiveSessionGuard();
        if (!guard.Acquired)
        {
            Console.Error.WriteLine("error: another Skeer autoSTEM run is already controlling Ableton Live.");
            return 3;
        }

        var summary = await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Console.WriteLine();
        Console.WriteLine($"completed {summary.Completed}, skipped {summary.Skipped}, failed {summary.Failed}, remaining {summary.Remaining}");
        Console.WriteLine($"duration  {summary.Duration:hh\\:mm\\:ss}");
        if (summary.Stopped) Console.WriteLine($"stopped   {summary.StopReason}: {summary.StopMessage}");

        foreach (var job in queue.Jobs.Where(j => j.Status == JobStatus.Failed))
            Console.WriteLine($"  failed: {job.DisplayName}: {job.Error}");

        return summary.Failed > 0 ? 4 : 0;
    }

    /// <summary>
    /// Adds files to the saved queue without running them, so a batch can be prepared
    /// headlessly and then started from the desktop app.
    /// </summary>
    private static int Enqueue(string[] args)
    {
        var destination = Option(args, "--dest");
        if (string.IsNullOrWhiteSpace(destination))
        {
            Console.Error.WriteLine("usage: autostem enqueue <path...> --dest DIR [--stems all|drums|...] [--mode speed|quality]");
            return 1;
        }

        var inputs = args.Where(a => !a.StartsWith("--") && a != destination).ToArray();
        if (inputs.Length == 0) { Console.Error.WriteLine("error: no inputs given"); return 1; }

        var profile = new JobProfile(
            ParseStems(Option(args, "--stems") ?? "all"),
            (Option(args, "--mode") ?? "speed").StartsWith("qual", StringComparison.OrdinalIgnoreCase)
                ? ProcessingMode.HighQuality
                : ProcessingMode.HighSpeed);

        var full = Path.GetFullPath(destination);
        Directory.CreateDirectory(full);

        var queue = new QueueService(new QueueStore());
        queue.Load();
        var added = queue.AddPaths(inputs, recursive: true, profile, full);

        // Remember the folder the same way the app would, so the batch is ready to run.
        var settings = new SettingsStore().Load();
        settings.OutputFolder = full;
        new SettingsStore().Save(settings);

        Console.WriteLine($"queued {added} file(s) as {profile.Descriptor}");
        Console.WriteLine($"queue  : {queue.Jobs.Count} job(s)");
        Console.WriteLine($"output : {full}");
        return 0;
    }

    private static int Verify(string[] args)
    {
        var folder = args.FirstOrDefault();
        if (folder is null || !Directory.Exists(folder)) { Console.Error.WriteLine("error: result folder not found"); return 1; }

        var manifest = StemForge.Core.Publish.ManifestStore.Read(folder);
        if (manifest is null) { Console.Error.WriteLine($"error: no {StemForge.Core.Publish.JobManifest.FileName} in that folder"); return 1; }

        Console.WriteLine($"job        : {manifest.JobId}");
        Console.WriteLine($"profile    : {manifest.ProfileKey}");
        Console.WriteLine($"source     : {manifest.SourcePath}");
        Console.WriteLine($"complete   : {manifest.Complete}, intact: {manifest.IsIntact(folder)}");

        var stems = manifest.Stems;
        var files = manifest.Files.ToDictionary(
            f => Enum.Parse<Stems>(f.Stem),
            f => Path.Combine(folder, f.Name));

        var sourceInfo = AudioFileReader.ReadInfo(manifest.SourcePath);
        var report = StemVerifier.Verify(stems, files, sourceInfo, VerificationOptions.Default);

        foreach (var file in report.Files)
            Console.WriteLine($"  {file.FileName,-14} {file.Bytes,10} bytes  {file.DurationSeconds:F3}s  {(file.Ok ? "OK" : file.Problem)}");

        Console.WriteLine(report.Ok ? "verification: PASS" : "verification: FAIL - " + report.Summary);
        return report.Ok ? 0 : 5;
    }

    /// <summary>
    /// Runs the same tracks through All / Drums Only in both processing modes, each with a
    /// fresh destination, and reports separation time separately from copying/publishing.
    ///
    ///   stemforge bench &lt;short&gt; &lt;longer&gt; --dest DIR [--simulate]
    ///
    /// With no --simulate it drives the installed Ableton Live build for the real numbers.
    /// </summary>
    private static async Task<int> BenchAsync(string[] args)
    {
        var destination = Option(args, "--dest");
        var simulate = args.Contains("--simulate");
        var files = args.Where(a => !a.StartsWith("--") && a != destination).ToArray();

        if (string.IsNullOrWhiteSpace(destination) || files.Length == 0)
        {
            Console.Error.WriteLine("usage: stemforge bench <short> <longer> --dest DIR [--simulate]");
            return 1;
        }

        var profiles = new[]
        {
            new JobProfile(Stems.All, ProcessingMode.HighSpeed),
            new JobProfile(Stems.Drums, ProcessingMode.HighSpeed),
            new JobProfile(Stems.All, ProcessingMode.HighQuality),
            new JobProfile(Stems.Drums, ProcessingMode.HighQuality),
        };

        using var log = new RunLog();
        var settings = new SettingsStore().Load();
        settings.UseSimulatedRunner = simulate;
        settings.KeepAwakeDuringRun = false;

        string template = string.Empty;
        LiveInstall install;
        if (simulate)
        {
            install = new LiveInstall(string.Empty, "0.0", LiveInstall.SimulatedEdition);
        }
        else
        {
            install = LiveLocator.Locate() ?? throw new InvalidOperationException("Ableton Live was not found.");
            template = WorkingProjectBuilder.FindBaseTemplate(install)
                ?? throw new InvalidOperationException("Live's audio track template was not found.");
        }

        Console.WriteLine($"engine   : {(simulate ? "SIMULATED (numbers are not Ableton)" : install.DisplayName)}");
        Console.WriteLine($"log      : {log.FilePath}");
        Console.WriteLine();
        Console.WriteLine($"{"track",-34} {"stems",-10} {"mode",-12} {"separation",11} {"publish",9} {"total",9}");

        var rows = new List<(string Track, string Stems, string Mode, double Separation, double Publish)>();

        foreach (var file in files)
        {
            foreach (var profile in profiles)
            {
                // A fresh destination per run so no result can satisfy another measurement.
                var workDestination = Path.Combine(destination, $"{Path.GetFileNameWithoutExtension(file)}-{profile.Stems}-{profile.Mode}");

                var queue = new QueueService(new QueueStore(Path.Combine(destination, "bench-queue.json")));
                queue.AddPaths(new[] { file }, recursive: false, profile, workDestination);

                var engine = Engine(settings, log, args);
                var context = new BatchContext
                {
                    Settings = settings,
                    Install = install,
                    BaseTemplatePath = template,
                    Log = log,
                    PersistQueue = queue.Save,
                    Progress = new Progress<BatchProgress>(p =>
                    {
                        if (p.Stage == JobStage.Separating)
                            Console.WriteLine($"  .. {Path.GetFileName(file)} {profile.Stems}/{profile.Mode}: {p.Detail}");
                    }),
                };

                var started = System.Diagnostics.Stopwatch.StartNew();
                var summary = await new BatchRunner(engine).RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);
                started.Stop();

                var job = queue.Jobs.Single();
                var separation = job.LastSeparationSeconds ?? 0;
                var publish = Math.Max(0, started.Elapsed.TotalSeconds - separation);

                rows.Add((Path.GetFileName(file), profile.Stems.Describe(), profile.Mode.DisplayName(), separation, publish));

                Console.WriteLine($"{Path.GetFileName(file),-34} {profile.Stems.Describe(),-10} {profile.Mode.DisplayName(),-12} {separation,10:F1}s {publish,8:F1}s {started.Elapsed.TotalSeconds,8:F1}s");

                if (summary.Failed > 0)
                    Console.WriteLine($"  !! {job.Error}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Observed savings (separation time only):");
        foreach (var file in files.Select(Path.GetFileName).Distinct())
        {
            var speedAll = rows.FirstOrDefault(r => r.Track == file && r.Stems == "all stems" && r.Mode == "High Speed").Separation;
            var speedDrums = rows.FirstOrDefault(r => r.Track == file && r.Stems == "drums only" && r.Mode == "High Speed").Separation;
            var qualityAll = rows.FirstOrDefault(r => r.Track == file && r.Stems == "all stems" && r.Mode == "High Quality").Separation;
            var qualityDrums = rows.FirstOrDefault(r => r.Track == file && r.Stems == "drums only" && r.Mode == "High Quality").Separation;

            Console.WriteLine($"  {file}");
            Console.WriteLine($"    Drums Only vs All at High Speed  : {Describe(speedDrums, speedAll)}");
            Console.WriteLine($"    Drums Only vs All at High Quality: {Describe(qualityDrums, qualityAll)}");
        }

        return 0;

        static string Describe(double part, double whole)
        {
            if (whole <= 0 || part <= 0) return "not measured";
            var saving = (whole - part) / whole * 100.0;
            return $"{part:F1}s vs {whole:F1}s => {saving:F0}% less separation time (single observation, not a guarantee)";
        }
    }

    /// <summary>
    /// Activates a button by its accessible name. Used for GUI smoke tests (for example
    /// pressing Run without a mouse) and for driving this app the way Live is driven.
    /// </summary>
    private static int UiaInvoke(string[] args)
    {
        var windowText = Option(args, "--window") ?? "Skeer autoSTEM";
        var buttonText = Option(args, "--button");
        if (string.IsNullOrWhiteSpace(buttonText))
        {
            Console.Error.WriteLine("usage: autostem uia-invoke --button TEXT [--window TEXT]");
            return 1;
        }

        var windows = StemForge.Core.Live.Automation.WindowSearch.FindByTitle(windowText);
        if (windows.Count == 0) { Console.Error.WriteLine($"error: no window matching '{windowText}'"); return 1; }

        foreach (var window in windows)
        {
            var root = StemForge.Core.Live.Automation.LiveUi.FromHandle(window.Handle);
            if (root is null) continue;

            foreach (var element in StemForge.Core.Live.Automation.LiveUi.Descendants(root))
            {
                System.Windows.Automation.ControlType type;
                try { type = element.Current.ControlType; }
                catch { continue; }

                if (type != System.Windows.Automation.ControlType.Button
                    && type != System.Windows.Automation.ControlType.Hyperlink) continue;

                if (!StemForge.Core.Live.Automation.LiveUi.Name(element)
                        .Contains(buttonText, StringComparison.OrdinalIgnoreCase)) continue;

                var enabled = StemForge.Core.Live.Automation.LiveUi.IsEnabled(element);
                var invoked = enabled && StemForge.Core.Live.Automation.LiveUi.TryInvoke(element);
                Console.WriteLine($"'{StemForge.Core.Live.Automation.LiveUi.Name(element)}' enabled={enabled} invoked={invoked}");
                return invoked ? 0 : 2;
            }
        }

        Console.Error.WriteLine($"error: no button matching '{buttonText}'");
        return 1;
    }

    private static int DumpUi(string[] args)
    {
        var title = Option(args, "--window") ?? "Ableton";
        var depth = int.TryParse(Option(args, "--depth"), out var d) ? d : 6;

        var windows = StemForge.Core.Live.Automation.WindowSearch.FindByTitle(title);
        if (windows.Count == 0)
        {
            Console.Error.WriteLine($"error: no visible window with '{title}' in its title");
            foreach (var w in StemForge.Core.Live.Automation.WindowSearch.All().Where(w => !string.IsNullOrWhiteSpace(w.Title)))
                Console.Error.WriteLine($"  {w.ProcessId,7} {w.Title}");
            return 1;
        }

        foreach (var window in windows)
        {
            Console.WriteLine($"=== window '{window.Title}' (pid {window.ProcessId}, {window.ClassName}) ===");
            var root = StemForge.Core.Live.Automation.LiveUi.FromHandle(window.Handle);
            if (root is null) { Console.WriteLine("  <UI Automation could not attach>"); continue; }

            Console.WriteLine(StemForge.Core.Live.Automation.LiveUi.DumpTree(root, depth));

            Console.WriteLine("--- interactive elements ---");
            foreach (var element in StemForge.Core.Live.Automation.LiveUi.Descendants(root))
            {
                var name = StemForge.Core.Live.Automation.LiveUi.Name(element);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var toggle = StemForge.Core.Live.Automation.LiveUi.ToggleState(element);
                Console.WriteLine($"  '{name}'{(toggle is null ? string.Empty : $" [toggle={(toggle.Value ? "on" : "off")}]")}");
            }

            Console.WriteLine("--- other visible windows of this process ---");
            foreach (var other in StemForge.Core.Live.Automation.LiveProcessWindows
                         .OtherVisibleWindows(window.ProcessId, window.Handle))
                Console.WriteLine($"  '{other.Title}'");

            var menu = Option(args, "--open-menu");
            if (menu is not null)
            {
                Console.WriteLine($"--- opening menu '{menu}' ---");
                var opened = StemForge.Core.Live.Automation.LiveUi.TryOpenMenu(root, menu);
                Console.WriteLine($"  TryOpenMenu = {opened}");
                Thread.Sleep(700);

                Console.WriteLine("--- menu items visible on the desktop ---");
                PrintProcessMenuItems(window.ProcessId);

                foreach (var menuWindow in StemForge.Core.Live.Automation.WindowSearch.All()
                             .Where(w => w.Title.Contains("menu", StringComparison.OrdinalIgnoreCase)))
                    Console.WriteLine($"  window '{menuWindow.Title}' class={menuWindow.ClassName}");

                Console.WriteLine("--- popup windows (all classes, this process) ---");
                foreach (var w in StemForge.Core.Live.Automation.LiveProcessWindows.ForProcess(window.ProcessId))
                    Console.WriteLine($"  {w}");
            }
        }

        return 0;
    }

    /// <summary>
    /// Every menu element one process currently shows. Walking the whole desktop tree is
    /// how this probe used to hang: every element is a cross-process call, and the desktop
    /// holds thousands of them. Menus are top-level windows, so the process's own windows
    /// are enough.
    /// </summary>
    private static void PrintProcessMenuItems(int processId)
    {
        foreach (var window in StemForge.Core.Live.Automation.LiveProcessWindows.ForProcess(processId))
        {
            if (!window.Visible) continue;
            var root = StemForge.Core.Live.Automation.LiveUi.FromHandle(window.Handle);
            if (root is null) continue;

            foreach (var element in StemForge.Core.Live.Automation.LiveUi.Descendants(root, max: 600))
            {
                string type;
                try { type = element.Current.ControlType?.ProgrammaticName ?? "?"; }
                catch { continue; }

                if (type is not ("ControlType.MenuItem" or "ControlType.Menu")) continue;

                var name = StemForge.Core.Live.Automation.LiveUi.Name(element);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var enabled = StemForge.Core.Live.Automation.LiveUi.IsEnabled(element);
                var box = element.Current.BoundingRectangle;
                Console.WriteLine($"  {window.Handle:X} {type.Replace("ControlType.", string.Empty),-9} '{name}' enabled={enabled} rect=({box.X:F0},{box.Y:F0},{box.Width:F0},{box.Height:F0})");
            }
        }
    }

    /// <summary>
    /// Presses one named element three different ways and reports what the desktop looked
    /// like before and after, so a click that never moved anything is visible as such.
    /// </summary>
    private static int ClickProbe(string[] args)
    {
        var windowText = Option(args, "--window") ?? "Ableton";
        var target = Option(args, "--name");
        var mode = (Option(args, "--mode") ?? "post").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("usage: autostem click-probe --name TEXT [--window TEXT] [--mode post|real|invoke] [--close]");
            return 1;
        }

        var windows = StemForge.Core.Live.Automation.WindowSearch.FindByTitle(windowText)
            .Where(w => StemForge.Core.Live.Automation.LiveProcessWindows.IsAlive(w.ProcessId))
            .ToList();
        if (windows.Count == 0) { Console.Error.WriteLine($"error: no live window matching '{windowText}'"); return 1; }

        System.Windows.Automation.AutomationElement? found = null;
        IntPtr host = IntPtr.Zero;
        foreach (var window in windows)
        {
            var root = StemForge.Core.Live.Automation.LiveUi.FromHandle(window.Handle);
            if (root is null) continue;
            foreach (var element in StemForge.Core.Live.Automation.LiveUi.Descendants(root))
            {
                if (!StemForge.Core.Live.Automation.LiveUi.Name(element)
                        .Contains(target, StringComparison.OrdinalIgnoreCase)) continue;
                found = element;
                var handle = element.Current.NativeWindowHandle;
                host = handle != 0 ? new IntPtr(handle) : window.Handle;
                break;
            }
            if (found is not null) break;
        }

        if (found is null) { Console.Error.WriteLine($"error: no element matching '{target}'"); return 1; }

        var point = StemForge.Core.Live.Automation.LiveUi.ClickablePoint(found);
        var box = found.Current.BoundingRectangle;
        Console.WriteLine($"target '{StemForge.Core.Live.Automation.LiveUi.Name(found)}' " +
                          $"{found.Current.ControlType.ProgrammaticName} enabled={StemForge.Core.Live.Automation.LiveUi.IsEnabled(found)} " +
                          $"rect=({box.X:F0},{box.Y:F0},{box.Width:F0},{box.Height:F0}) click={point?.ToString() ?? "<none>"} host=0x{host:X}");

        var pid = windows[0].ProcessId;

        Console.WriteLine("--- menus before ---");
        PrintProcessMenuItems(pid);

        if (point is null) { Console.Error.WriteLine("error: element has no clickable point"); return 2; }

        if (HasFlag(args, "--activate"))
            Console.WriteLine($"Activate = {StemForge.Core.Live.Automation.WindowControl.Activate(host)}");

        var done = mode switch
        {
            "post" or "send" => PostedInput.Click(host, point.Value.X, point.Value.Y, mode == "send"),
            "real" => RunRealClick(host, point.Value),
            "invoke" => StemForge.Core.Live.Automation.LiveUi.TryInvoke(found),
            _ => false,
        };
        Console.WriteLine($"mode={mode} delivered={done}");
        Thread.Sleep(800);

        Console.WriteLine("--- menus after ---");
        PrintProcessMenuItems(pid);

        Console.WriteLine("--- windows of the target process after ---");
        foreach (var window in windows)
            foreach (var w in StemForge.Core.Live.Automation.LiveProcessWindows.ForProcess(window.ProcessId))
                Console.WriteLine($"  {w}");

        if (HasFlag(args, "--close"))
            Console.WriteLine($"Escape posted = {PostedInput.Escape(host)}");

        return done ? 0 : 2;
    }

    /// <summary>
    /// A real click, through the same door the product uses: <c>SendInput</c> is not reachable from
    /// here any more, and a probe that skipped the guard would report a click the product is not
    /// allowed to make. A refusal prints its reason, because "the guard said no" is the answer this
    /// probe exists to find out.
    /// </summary>
    private static bool RunRealClick(IntPtr host, System.Drawing.Point point)
    {
        var result = StemForge.Core.Live.Automation.GuardedInput.Click(host, point, "clicked the probed point");
        Console.WriteLine($"real click: {result.Sentence}");
        return result.Injected;
    }

    /// <summary>
    /// The engine for this command: --engine wins over what the settings remember, and
    /// --simulate replaces whatever was chosen with the test double.
    /// </summary>
    private static ISeparator Engine(AppSettings settings, RunLog log, string[] args)
    {
        var kind = ParseEngine(Option(args, "--engine")) ?? settings.Engine;
        return SeparatorFactory.CreateForRun(kind, log, args.Contains("--simulate"));
    }

    private static EngineKind? ParseEngine(string? value) => value?.ToLowerInvariant() switch
    {
        null => null,
        "live" or "ableton" => EngineKind.Live,
        "local" or "model" => EngineKind.Local,
        "cloud" => EngineKind.Cloud,
        _ => throw new ArgumentException($"unknown engine '{value}' (live, local, cloud)"),
    };

    /// <summary>
    /// The value after <paramref name="name"/>.
    ///
    /// A name with nothing after it used to read as "not given", which is how a probe run as
    /// <c>--mode post --close</c> reported a clean result having closed nothing and taken no
    /// screenshot of the aftermath. These probes get cited as evidence, so an argument that cannot
    /// be honoured is an error rather than a default.
    /// </summary>
    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException(
                index + 1 >= args.Length
                    ? $"'{name}' needs a value, and it was the last argument"
                    : $"'{name}' needs a value, not another switch ('{args[index + 1]}')");

        return args[index + 1];
    }

    /// <summary>
    /// A switch that takes no value. Kept apart from <see cref="Option"/> on purpose: reading a
    /// switch through the value accessor is what made a trailing one invisible, because the token
    /// after it belongs to something else or to nothing.
    /// </summary>
    private static bool HasFlag(string[] args, string name) => args.Contains(name);

    private static Stems ParseStems(string value)
    {
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase)) return Stems.All;
        if (value.Equals("drums", StringComparison.OrdinalIgnoreCase)) return Stems.Drums;

        var result = Stems.None;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToLowerInvariant() switch
            {
                "vocals" or "vox" => Stems.Vocals,
                "drums" => Stems.Drums,
                "bass" => Stems.Bass,
                "others" or "other" => Stems.Others,
                _ => throw new ArgumentException($"unknown stem '{part}'"),
            };
        }

        return result == Stems.None ? throw new ArgumentException("at least one stem is required") : result;
    }
}

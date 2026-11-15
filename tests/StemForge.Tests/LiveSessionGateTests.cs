using System.Diagnostics;
using System.Text.Json;
using StemForge.Core.Live;
using StemForge.Core.Support;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The preflight that decides whether an already-running Ableton Live may be closed. Getting it
/// wrong in one direction stalls every batch; getting it wrong in the other destroys the user's
/// unsaved work, so both directions are pinned down here.
/// </summary>
public sealed class LiveSessionGateTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("", false)]
    [InlineData("   ", true)]
    public void AWindowTitleThatHasNotArrivedYetMeansLiveIsStillStarting(string title, bool launchedByUs)
    {
        Assert.Equal(LiveSessionKind.Starting, LiveSessionClassifier.Classify(title, launchedByUs));
    }

    [Theory]
    [InlineData("autoSTEM Working Set.als - Ableton Live 12 Suite", true)]
    [InlineData("autoSTEM Working Set.als - Ableton Live 12 Suite", false)]
    [InlineData("AUTOSTEM WORKING SET - Ableton Live 12 Suite", false)]
    public void OurOwnWorkingSetIsRecognisedWhoeverAppearsToHaveOpenedIt(string title, bool launchedByUs)
    {
        Assert.Equal(LiveSessionKind.OwnWorkingSet, LiveSessionClassifier.Classify(title, launchedByUs));
    }

    [Theory]
    [InlineData("Untitled - Ableton Live 12 Suite")]
    [InlineData("Untitled* - Ableton Live 12 Suite")]
    public void AnEmptySessionIsOursOnlyWhenWeAreTheOnesWhoLaunchedIt(string title)
    {
        Assert.Equal(LiveSessionKind.OwnOrphan, LiveSessionClassifier.Classify(title, launchedByUs: true));
        Assert.Equal(LiveSessionKind.Foreign, LiveSessionClassifier.Classify(title, launchedByUs: false));
    }

    [Fact]
    public void ANamedProjectStaysForeignEvenInALiveWeLaunched()
    {
        // A user can open their own set in the instance we started. Ownership of the process is
        // not ownership of the work in it.
        Assert.Equal(LiveSessionKind.Foreign, LiveSessionClassifier.Classify("My Song.als - Ableton Live 12 Suite", launchedByUs: true));
    }

    [Fact]
    public void TheLaunchRecordSurvivesARoundTrip()
    {
        using var workspace = new TempWorkspace();
        using var live = new FakeProcess("Ableton Live 12 Suite");

        LiveLaunchRecord.Write(live.Process, @"C:\Program Files\Ableton\Live.exe");
        var record = LiveLaunchRecord.ReadCurrent();

        Assert.NotNull(record);
        Assert.Equal(live.Process.Id, record!.ProcessId);
        Assert.Equal(live.Process.StartTime.ToUniversalTime().Ticks, record.StartedUtcTicks);
    }

    [Fact]
    public void ARecordForAProcessThatHasExitedIsIgnored()
    {
        using var workspace = new TempWorkspace();
        using var live = new FakeProcess("Ableton Live 12 Suite");
        var started = live.Process.StartTime.ToUniversalTime().Ticks;

        live.Process.Kill();
        live.Process.WaitForExit(5000);
        WriteRecord(live.Process.Id, started);

        Assert.Null(LiveLaunchRecord.ReadCurrent());
    }

    [Fact]
    public void ARecordWhoseStartTimeDoesNotMatchIsIgnored()
    {
        using var workspace = new TempWorkspace();
        using var live = new FakeProcess("Ableton Live 12 Suite");

        // Same pid, different process: the number was recycled. Attributing it to us would let a
        // later run close a Live that belongs to the user.
        WriteRecord(live.Process.Id, live.Process.StartTime.ToUniversalTime().Ticks + 1);

        Assert.Null(LiveLaunchRecord.ReadCurrent());
    }

    [Fact]
    public void ARecordWhoseProcessIsNotLiveIsIgnored()
    {
        using var workspace = new TempWorkspace();
        using var notLive = new FakeProcess("SomeOtherApplication");

        WriteRecord(notLive.Process.Id, notLive.Process.StartTime.ToUniversalTime().Ticks);

        Assert.Null(LiveLaunchRecord.ReadCurrent());
    }

    [Fact]
    public void ClearingTheRecordLeavesNothingToAttribute()
    {
        using var workspace = new TempWorkspace();
        using var live = new FakeProcess("Ableton Live 12 Suite");

        LiveLaunchRecord.Write(live.Process, @"C:\Program Files\Ableton\Live.exe");
        Assert.NotNull(LiveLaunchRecord.ReadCurrent());

        LiveLaunchRecord.Clear();
        Assert.Null(LiveLaunchRecord.ReadCurrent());
    }

    [Fact]
    public void NoRecordAtAllMeansNothingWasLaunchedByUs()
    {
        using var workspace = new TempWorkspace();

        Assert.Null(LiveLaunchRecord.ReadCurrent());
    }

    private static void WriteRecord(int pid, long startedUtcTicks)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.LiveLaunchRecordFile,
            JsonSerializer.Serialize(new LiveLaunchRecord(pid, startedUtcTicks, @"C:\Program Files\Ableton\Live.exe")));
    }

    /// <summary>
    /// A real running process whose file name decides what the record code makes of it, which is
    /// the only thing it has to go on besides the pid and the start time. Long enough to outlive
    /// the assertions, killed on dispose.
    /// </summary>
    private sealed class FakeProcess : IDisposable
    {
        private readonly string _folder;

        public FakeProcess(string name)
        {
            _folder = Path.Combine(Path.GetTempPath(), $"StemForge.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_folder);
            var exe = Path.Combine(_folder, $"{name}.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), exe);

            Process = System.Diagnostics.Process.Start(new ProcessStartInfo(exe, "-n 60 127.0.0.1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;

            _ = Process.StartTime;
        }

        public System.Diagnostics.Process Process { get; }

        public void Dispose()
        {
            try
            {
                if (!Process.HasExited) Process.Kill(entireProcessTree: true);
                Process.Dispose();
            }
            catch
            {
                // The test already made its point; cleanup is best effort.
            }

            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch
            {
                // ditto
            }
        }
    }
}

using StemForge.Core.Engines;
using StemForge.Core.Live;
using StemForge.Core.Logging;
using StemForge.Core.Models;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The seam between engines: which failures an engine is allowed to report, and what happens
/// when a machine is asked for an engine it does not have. Both decide whether a paused batch
/// stops for a reason somebody can act on.
/// </summary>
public sealed class EngineSeamTests
{
    [Theory]
    [InlineData(ErrorKind.LiveNotInstalled)]
    [InlineData(ErrorKind.LiveVersionUnsupported)]
    [InlineData(ErrorKind.LiveStartupFailed)]
    [InlineData(ErrorKind.LiveWindowNotFound)]
    [InlineData(ErrorKind.LiveLostFocus)]
    [InlineData(ErrorKind.LiveCrashed)]
    [InlineData(ErrorKind.DesktopLocked)]
    [InlineData(ErrorKind.UnexpectedDialog)]
    [InlineData(ErrorKind.LiveErrorDialog)]
    [InlineData(ErrorKind.ClipNotSelected)]
    [InlineData(ErrorKind.AnotherRunnerActive)]
    public void OnlyAnEngineThatDrivesLiveCanReportThese(ErrorKind kind)
    {
        Assert.True(EngineFailures.IsLiveOnly(kind), $"{kind} should be Live-only");
        Assert.Equal(kind, EngineFailures.Normalize(EngineKind.Live, kind));
        Assert.Equal(ErrorKind.Unknown, EngineFailures.Normalize(EngineKind.Local, kind));
    }

    [Theory]
    [InlineData(ErrorKind.StemsMissing)]
    [InlineData(ErrorKind.StemsUnverified)]
    [InlineData(ErrorKind.SeparationTimeout)]
    [InlineData(ErrorKind.SeparationCancelled)]
    [InlineData(ErrorKind.PublishFailed)]
    [InlineData(ErrorKind.DiskFull)]
    [InlineData(ErrorKind.SourceChanged)]
    [InlineData(ErrorKind.Cancelled)]
    public void TheseBelongToAnyEngine(ErrorKind kind)
    {
        Assert.False(EngineFailures.IsLiveOnly(kind), $"{kind} is not a Live-only failure");
        Assert.Equal(kind, EngineFailures.Normalize(EngineKind.Local, kind));
    }

    [Fact]
    public void TheCatalogueSaysWhatThisBuildCanActuallyRun()
    {
        Assert.Equal(EngineKind.Live, Assert.Single(SeparatorFactory.Catalogue, info => info.IsImplemented).Kind);

        foreach (var info in SeparatorFactory.Catalogue)
            Assert.Equal(SeparatorFactory.IsImplemented(info.Kind), info.IsImplemented);
    }

    [Fact]
    public void TheLiveEngineComesFromTheFactory()
    {
        using var workspace = new TempWorkspace();
        using var log = new RunLog(workspace.Path("run.log"));

        ISeparator engine = SeparatorFactory.Create(EngineKind.Live, log);

        Assert.Equal(EngineKind.Live, engine.Kind);
        Assert.NotEmpty(engine.Name);
    }

    [Fact]
    public void AskingForAnEngineThatIsNotHereSaysSoRatherThanSilentlyUsingLive()
    {
        using var workspace = new TempWorkspace();
        using var log = new RunLog(workspace.Path("run.log"));

        var error = Assert.Throws<SeparatorFactory.EngineUnavailableException>(
            () => SeparatorFactory.Create(EngineKind.Local, log));

        Assert.Contains("not in this build", error.Message);
    }

    [Fact]
    public void TheSimulatedDoubleReplacesTheEngineOnlyWhenItWasAskedFor()
    {
        using var workspace = new TempWorkspace();
        using var log = new RunLog(workspace.Path("run.log"));

        Assert.IsType<SimulatedLiveRunner>(SeparatorFactory.CreateForRun(EngineKind.Live, log, simulate: true));
        Assert.IsType<LiveAutomationRunner>(SeparatorFactory.CreateForRun(EngineKind.Live, log, simulate: false));
    }

    [Fact]
    public void TheSettingStillDefaultsToTheOnlyEngineThatExists()
    {
        Assert.Equal(EngineKind.Live, new AppSettings().Engine);
    }

    /// <summary>
    /// A queue file names the cause that stopped a job. A name this build does not have is not
    /// forgiven and not skipped: deserialising throws, the file is moved aside as
    /// <c>.corrupt-*</c> and the whole queue loads empty - so renaming or deleting one of these is
    /// how somebody's saved batch disappears. QueuePersistenceTests measures both halves of that;
    /// this check is the guard that keeps the first half from ever happening.
    ///
    /// What it deliberately does NOT protect is the order. The list is the set of names this
    /// repository holds at HEAD (5b1c340), in the shape a queue file can contain them, and a new
    /// cause may be placed wherever it belongs. The check that used to sit here compared two
    /// ordinals on the stated premise that queue files store these values positionally; that premise
    /// is false, so the premise went and the guard now asks the question the truth decides.
    /// </summary>
    [Fact]
    public void EveryFailureCauseAQueueCanNameStillExists()
    {
        string[] released =
        [
            "None", "SourceMissing", "SourceChanged", "UnsupportedFormat", "SourceUnreadable",
            "DestinationUnwritable", "DestinationMissing", "DiskFull",
            "LiveNotInstalled", "LiveVersionUnsupported", "LiveAlreadyRunningWithUserProject",
            "LiveStartupFailed", "LiveWindowNotFound", "LiveLostFocus", "DesktopLocked",
            "LiveCrashed", "UnexpectedDialog", "LiveErrorDialog", "SeparationTimeout",
            "SeparationCancelled", "StemsMissing", "StemsUnverified", "PublishFailed",
            "AnotherRunnerActive", "Cancelled", "Unknown", "ClipNotSelected",
        ];

        var present = Enum.GetNames<ErrorKind>();
        var missing = released.Where(name => !present.Contains(name, StringComparer.Ordinal)).ToArray();

        Assert.True(missing.Length == 0,
            $"a saved queue naming {string.Join(", ", missing)} would no longer load; " +
            "the cause has to stay reachable by name, or old files are quarantined as .corrupt-*");
    }
}

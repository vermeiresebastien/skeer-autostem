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
    /// A queue on disk names failures by their number, so a new cause has to be added at the end
    /// or every saved row from an older build changes meaning. This is the guard for that.
    /// </summary>
    [Fact]
    public void NewFailureCausesAreAppendedRatherThanInserted()
    {
        Assert.True((int)ErrorKind.ClipNotSelected > (int)ErrorKind.Unknown,
            "ErrorKind members are persisted positionally; a new cause must come after Unknown");
    }
}

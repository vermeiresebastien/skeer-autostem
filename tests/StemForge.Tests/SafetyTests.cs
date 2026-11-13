using StemForge.Core.Live;
using StemForge.Core.Logging;
using StemForge.Core.Models;
using StemForge.Core.Orchestration;
using StemForge.Core.Queue;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The batch must stop sending work to the engine as soon as the environment becomes
/// unsafe, and it must leave the queue intact so nothing is lost.
/// </summary>
public sealed class SafetyTests
{
    [Theory]
    [InlineData(SimulatedLiveRunner.FaultMode.LiveCrashed)]
    [InlineData(SimulatedLiveRunner.FaultMode.LostFocus)]
    [InlineData(SimulatedLiveRunner.FaultMode.UnexpectedDialog)]
    [InlineData(SimulatedLiveRunner.FaultMode.LiveStartupFailed)]
    [InlineData(SimulatedLiveRunner.FaultMode.SeparationTimeout)]
    public async Task AnUnsafeStatePausesTheBatchAndPreservesTheQueue(SimulatedLiveRunner.FaultMode fault)
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var queue = new QueueService(new QueueStore(workspace.Path("queue.json")));
        queue.AddPaths(new[] { workspace.CreateWav("a.wav"), workspace.CreateWav("b.wav") }, false, JobProfile.Default, destination);

        var log = new RunLog(workspace.Path("run.log"));
        var context = new BatchContext
        {
            Settings = new AppSettings { KeepAwakeDuringRun = false },
            Install = new LiveInstall("simulated", "0.0", "Simulated"),
            BaseTemplatePath = workspace.Path("unused.als"),
            Log = log,
            PersistQueue = queue.Save,
        };

        var audit = new List<string>();
        var engine = new SimulatedLiveRunner(log, request =>
        {
            audit.Add(request.Job.Id);
            return new SimulatedLiveRunner.Options { Fault = fault };
        });

        var summary = await new BatchRunner(engine).RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Assert.True(summary.Stopped,
            $"stopped={summary.Stopped} reason={summary.StopReason} failed={summary.Failed} audit={audit.Count}");
        Assert.Single(audit);                                 // the second job was never sent
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, queue.Jobs.Count(j => j.Status == JobStatus.Pending));
        Assert.Equal(fault switch
        {
            SimulatedLiveRunner.FaultMode.LiveCrashed => ErrorKind.LiveCrashed,
            SimulatedLiveRunner.FaultMode.LostFocus => ErrorKind.LiveLostFocus,
            SimulatedLiveRunner.FaultMode.UnexpectedDialog => ErrorKind.UnexpectedDialog,
            SimulatedLiveRunner.FaultMode.LiveStartupFailed => ErrorKind.LiveStartupFailed,
            _ => ErrorKind.SeparationTimeout,
        }, summary.StopReason);

        // Nothing was published for the failed job.
        Assert.DoesNotContain(queue.Jobs, j => j.ResultFolder is not null);
    }

    [Fact]
    public async Task AHigherQualityResultNeverSatisfiesTheDefaultProfile()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("track.wav", seconds: 1.0);

        var queue = new QueueService(new QueueStore(workspace.Path("queue.json")));
        queue.AddPaths(new[] { source }, false, new JobProfile(Stems.Drums, ProcessingMode.HighQuality), destination);

        var log = new RunLog(workspace.Path("run.log"));
        var context = new BatchContext
        {
            Settings = new AppSettings { KeepAwakeDuringRun = false },
            Install = new LiveInstall("simulated", "0.0", "Simulated"),
            BaseTemplatePath = workspace.Path("unused.als"),
            Log = log,
            PersistQueue = queue.Save,
        };

        var runner = new BatchRunner(new SimulatedLiveRunner(log));
        await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        var drumsOnly = queue.Jobs.Single(j => j.Profile.Key != JobProfile.Default.Key);

        // Now queue the very same track as All Stems / High Speed.
        queue.AddPaths(new[] { source }, false, JobProfile.Default, destination);
        Assert.Equal(2, queue.Jobs.Count);

        await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        var allStems = queue.Jobs.Single(j => j.Profile.Key == JobProfile.Default.Key);

        // The different profile was genuinely processed, not satisfied by the Drums Only result.
        Assert.Equal(1, allStems.Attempts);
        Assert.Equal(JobStatus.Completed, allStems.Status);
        Assert.Equal(4, allStems.OutputFiles.Count);
        Assert.NotNull(allStems.ResultFolder);
        Assert.NotEqual(drumsOnly.ResultFolder, allStems.ResultFolder);
    }
}

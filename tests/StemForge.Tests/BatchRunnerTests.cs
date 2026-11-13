using StemForge.Core.Models;
using StemForge.Core.Orchestration;
using StemForge.Core.Queue;
using StemForge.Core.Live;
using StemForge.Core.Logging;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// End-to-end tests of the queue against the simulated engine. These cover resume,
/// skip-on-completed, ContinueOnError semantics and stop-after-current. They do NOT
/// constitute Ableton verification - see the test report.
/// </summary>
public sealed class BatchRunnerTests
{
    private static BatchContext Context(TempWorkspace workspace, RunLog log, QueueService queue, IProgress<BatchProgress>? progress = null)
    {
        return new BatchContext
        {
            Settings = new AppSettings { KeepAwakeDuringRun = false, RecursiveImport = false },
            Install = new LiveInstall("simulated", "0.0", "Simulated"),
            BaseTemplatePath = workspace.Path("unused.als"),
            Log = log,
            PersistQueue = queue.Save,
            Progress = progress,
        };
    }

    private static (QueueService queue, string destination) BuildQueue(TempWorkspace workspace, params string[] sources)
    {
        var queue = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var destination = workspace.NewFolder("out");
        queue.AddPaths(sources, recursive: false, JobProfile.Default, destination);
        return (queue, destination);
    }

    [Fact]
    public async Task AShortAndALongerTrackBothComplete()
    {
        using var workspace = new TempWorkspace();
        var shortTrack = workspace.CreateWav("short.wav", seconds: 1.0);
        var longTrack = workspace.CreateWav("longer.wav", seconds: 6.0);
        var (queue, destination) = BuildQueue(workspace, shortTrack, longTrack);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);
        var runner = new BatchRunner(new SimulatedLiveRunner(log));

        var summary = await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Assert.Equal(2, summary.Completed);
        Assert.Equal(0, summary.Failed);
        Assert.All(queue.Jobs, j => Assert.Equal(JobStatus.Completed, j.Status));
        Assert.True(Directory.Exists(Path.Combine(destination, "short")));
        Assert.True(File.Exists(Path.Combine(destination, "short", "vocals.wav")));
    }

    [Fact]
    public async Task ARerunSkipsAlreadyVerifiedWork()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateWav("track.wav", seconds: 1.0);
        var (queue, destination) = BuildQueue(workspace, source);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);
        var runner = new BatchRunner(new SimulatedLiveRunner(log));

        var first = await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);
        Assert.Equal(1, first.Completed);

        var attemptsAfterFirstRun = queue.Jobs.Single().Attempts;
        var folderAfterFirstRun = queue.Jobs.Single().ResultFolder;

        var second = await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        // Nothing was reprocessed: the verified result on disk still satisfies the job.
        Assert.Equal(attemptsAfterFirstRun, queue.Jobs.Single().Attempts);
        Assert.Equal(folderAfterFirstRun, queue.Jobs.Single().ResultFolder);
        Assert.Equal(JobStatus.Completed, queue.Jobs.Single().Status);
        Assert.Equal(1, second.Completed);
        Assert.True(Directory.Exists(Path.Combine(destination, folderAfterFirstRun!)));
    }

    [Fact]
    public async Task ACompletedJobWhoseOutputsVanishedIsReprocessed()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateWav("track.wav", seconds: 1.0);
        var (queue, destination) = BuildQueue(workspace, source);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);
        var runner = new BatchRunner(new SimulatedLiveRunner(log));

        await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);
        var attempts = queue.Jobs.Single().Attempts;

        // Someone deletes the published result.
        Directory.Delete(Path.Combine(destination, queue.Jobs.Single().ResultFolder!), recursive: true);

        await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Assert.Equal(attempts + 1, queue.Jobs.Single().Attempts);
        Assert.Equal(JobStatus.Completed, queue.Jobs.Single().Status);
    }

    [Fact]
    public async Task AnUnsupportedFileFailsClearlyAndTheNextTrackStillRuns()
    {
        using var workspace = new TempWorkspace();
        var bad = workspace.Path("broken.wav");
        File.WriteAllText(bad, "not really audio");

        var good = workspace.CreateWav("good.wav", seconds: 1.0);

        var queue = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var destination = workspace.NewFolder("out");
        // Added directly, so the unsupported file is queued and must fail at run time.
        queue.AddPaths(new[] { bad, good }, recursive: false, JobProfile.Default, destination);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);
        var runner = new BatchRunner(new SimulatedLiveRunner(log));

        var summary = await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Assert.Equal(1, summary.Completed);
        Assert.Equal(1, summary.Failed);

        var failed = queue.Jobs.Single(j => j.Status == JobStatus.Failed);
        Assert.Equal(ErrorKind.UnsupportedFormat, failed.ErrorKind);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));
    }

    [Fact]
    public async Task AMissingSourceFailsWithoutStoppingTheBatch()
    {
        using var workspace = new TempWorkspace();
        var gone = workspace.CreateWav("gone.wav", seconds: 1.0);
        var good = workspace.CreateWav("good.wav", seconds: 1.0);

        var queue = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var destination = workspace.NewFolder("out");
        queue.AddPaths(new[] { gone, good }, recursive: false, JobProfile.Default, destination);

        File.Delete(gone);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);
        var runner = new BatchRunner(new SimulatedLiveRunner(log));

        var summary = await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Assert.Equal(1, summary.Completed);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(ErrorKind.SourceMissing, queue.Jobs.Single(j => j.Status == JobStatus.Failed).ErrorKind);
    }

    [Fact]
    public async Task AChangedSourceIsDetectedRatherThanProcessed()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateWav("track.wav", seconds: 1.0);
        var (queue, _) = BuildQueue(workspace, source);

        // The file is replaced after it was queued.
        workspace.CreateWav("track.wav", seconds: 2.0);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(3));

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);
        var runner = new BatchRunner(new SimulatedLiveRunner(log));

        await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        var job = queue.Jobs.Single();
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(ErrorKind.SourceChanged, job.ErrorKind);
    }

    [Fact]
    public async Task StopAfterCurrentFinishesTheActiveJobAndPreservesTheRest()
    {
        using var workspace = new TempWorkspace();
        var a = workspace.CreateWav("a.wav", seconds: 1.0);
        var b = workspace.CreateWav("b.wav", seconds: 1.0);
        var c = workspace.CreateWav("c.wav", seconds: 1.0);
        var (queue, _) = BuildQueue(workspace, a, b, c);

        var log = new RunLog(workspace.Path("run.log"));
        BatchRunner? runner = null;

        // The user presses "Stop After Current Track" while the first track is processing.
        var calls = 0;
        var engine = new SimulatedLiveRunner(log, _ =>
        {
            calls++;
            if (calls == 1) runner!.StopAfterCurrent = true;
            return new SimulatedLiveRunner.Options();
        });
        runner = new BatchRunner(engine);

        var context = Context(workspace, log, queue);

        var summary = await runner.RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Assert.True(summary.Stopped, $"stopped={summary.Stopped} completed={summary.Completed}");
        Assert.Equal(1, summary.Completed);
        Assert.Equal(2, queue.Jobs.Count(j => j.Status == JobStatus.Pending));
        Assert.Equal(1, queue.Jobs.Count(j => j.Status == JobStatus.Completed));
    }

    [Fact]
    public async Task AnInterruptedRunningJobResumesOnTheNextRun()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateWav("track.wav", seconds: 1.0);
        var (queue, _) = BuildQueue(workspace, source);

        // Simulate a crash mid-job: the queue on disk says Running.
        queue.Jobs[0].Status = JobStatus.Running;
        queue.Jobs[0].Stage = JobStage.Separating;
        queue.Save();

        var reloaded = new QueueService(new QueueStore(workspace.Path("queue.json")));
        reloaded.Load();
        Assert.Equal(JobStatus.Pending, reloaded.Jobs.Single().Status);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, reloaded);

        var summary = await new BatchRunner(new SimulatedLiveRunner(log))
            .RunAsync(reloaded.Jobs.ToList(), context, CancellationToken.None);

        Assert.Equal(1, summary.Completed);
    }

    [Fact]
    public async Task AFatalEngineFailurePausesTheBatchAndPreservesTheQueue()
    {
        using var workspace = new TempWorkspace();
        var a = workspace.CreateWav("a.wav", seconds: 1.0);
        var b = workspace.CreateWav("b.wav", seconds: 1.0);
        var (queue, _) = BuildQueue(workspace, a, b);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);
        var engine = new SimulatedLiveRunner(log, _ => new SimulatedLiveRunner.Options
        {
            Fault = SimulatedLiveRunner.FaultMode.LiveCrashed,
        });

        var summary = await new BatchRunner(engine).RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Assert.True(summary.Stopped, $"stopped={summary.Stopped} reason={summary.StopReason} failed={summary.Failed}");
        Assert.Equal(ErrorKind.LiveCrashed, summary.StopReason);
        Assert.Equal(1, summary.Failed);
        // The second job is untouched and still queued.
        Assert.Equal(1, queue.Jobs.Count(j => j.Status == JobStatus.Pending));
        Assert.Equal(1, queue.Jobs.Count(j => j.Status == JobStatus.Failed));
    }

    [Fact]
    public async Task AFaultyTrackDoesNotStopTheBatchWhenTheStateIsKnown()
    {
        using var workspace = new TempWorkspace();
        var a = workspace.CreateWav("a.wav", seconds: 1.0);
        var b = workspace.CreateWav("b.wav", seconds: 1.0);
        var (queue, _) = BuildQueue(workspace, a, b);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);

        var calls = 0;
        var engine = new SimulatedLiveRunner(log, _ =>
        {
            calls++;
            return new SimulatedLiveRunner.Options
            {
                Fault = calls == 1 ? SimulatedLiveRunner.FaultMode.NoStems : SimulatedLiveRunner.FaultMode.None,
            };
        });

        var summary = await new BatchRunner(engine).RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Assert.False(summary.Stopped);
        Assert.Equal(1, summary.Completed);
        Assert.Equal(1, summary.Failed);
    }

    [Fact]
    public async Task ADifferentModeProducesASeparateJobForTheSameTrack()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateWav("track.wav", seconds: 1.0);
        var destination = workspace.NewFolder("out");

        var queue = new QueueService(new QueueStore(workspace.Path("queue.json")));
        queue.AddPaths(new[] { source }, false, new JobProfile(Stems.Drums, ProcessingMode.HighSpeed), destination);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);

        await new BatchRunner(new SimulatedLiveRunner(log)).RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        // Add the same track again in High Quality mode.
        queue.AddPaths(new[] { source }, false, new JobProfile(Stems.Drums, ProcessingMode.HighQuality), destination);
        await new BatchRunner(new SimulatedLiveRunner(log)).RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        var completed = queue.Jobs.Where(j => j.Status == JobStatus.Completed).ToList();
        Assert.Equal(2, completed.Count);
        Assert.NotEqual(completed[0].ResultFolder, completed[1].ResultFolder);
    }

    [Fact]
    public async Task TwoFilesWithTheSameBasenameLandInDistinctFolders()
    {
        using var workspace = new TempWorkspace();
        workspace.NewFolder("a");
        workspace.NewFolder("b");
        var a = workspace.CreateWav(Path.Combine("a", "Track.wav"), seconds: 1.0);
        var b = workspace.CreateWav(Path.Combine("b", "Track.wav"), seconds: 1.0);

        var (queue, _) = BuildQueue(workspace, a, b);
        Assert.Equal(2, queue.Jobs.Count);

        var log = new RunLog(workspace.Path("run.log"));
        var context = Context(workspace, log, queue);
        await new BatchRunner(new SimulatedLiveRunner(log)).RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        var folders = queue.Jobs.Select(j => j.ResultFolder).ToList();
        Assert.Equal(2, folders.Distinct().Count());
    }
}

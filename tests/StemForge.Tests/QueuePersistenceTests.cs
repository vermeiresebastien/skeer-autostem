using StemForge.Core.Models;
using StemForge.Core.Queue;
using StemForge.Core.Support;
using Xunit;

namespace StemForge.Tests;

public sealed class QueuePersistenceTests
{
    private static Job MakeJob(string source, JobProfile profile, string destination) => new()
    {
        Id = "job-test",
        SourcePath = source,
        SourceSizeBytes = 1234,
        SourceLastWriteUtcTicks = 987654321,
        SourceQuickHash = "abcdef",
        Stems = profile.Stems,
        Mode = profile.Mode,
        DestinationRoot = destination,
        Status = JobStatus.Pending,
    };

    [Fact]
    public void SaveAndLoadRoundTripsEveryField()
    {
        using var workspace = new TempWorkspace();
        var store = new QueueStore(workspace.Path("queue.json"));

        var job = MakeJob(workspace.Path("track.wav"), new JobProfile(Stems.Drums, ProcessingMode.HighQuality), workspace.Root);
        job.Status = JobStatus.Completed;
        job.ResultFolder = "Track (Drums)";
        job.OutputFiles = new List<string> { "drums.wav" };
        job.LastSeparationSeconds = 12.5;

        store.Save(new[] { job });
        var loaded = store.Load();

        var round = Assert.Single(loaded);
        Assert.Equal(job.Id, round.Id);
        Assert.Equal(Stems.Drums, round.Stems);
        Assert.Equal(ProcessingMode.HighQuality, round.Mode);
        Assert.Equal(job.SourcePath, round.SourcePath);
        Assert.Equal(job.SourceQuickHash, round.SourceQuickHash);
        Assert.Equal(JobStatus.Completed, round.Status);
        Assert.Equal("Track (Drums)", round.ResultFolder);
        Assert.Equal(new[] { "drums.wav" }, round.OutputFiles);
        Assert.Equal(12.5, round.LastSeparationSeconds);
    }

    [Fact]
    public void AnInterruptedRunningJobComesBackAsPending()
    {
        using var workspace = new TempWorkspace();
        var store = new QueueStore(workspace.Path("queue.json"));

        var job = MakeJob(workspace.Path("track.wav"), JobProfile.Default, workspace.Root);
        job.Status = JobStatus.Running;
        job.Stage = JobStage.Separating;

        store.Save(new[] { job });
        var loaded = store.Load();

        var round = Assert.Single(loaded);
        Assert.Equal(JobStatus.Pending, round.Status);
        Assert.Equal(JobStage.Queued, round.Stage);
        Assert.NotNull(round.Error);
        Assert.Contains("Interrupted", round.Error);
    }

    [Fact]
    public void CompletedJobsAreNotResetByLoad()
    {
        using var workspace = new TempWorkspace();
        var store = new QueueStore(workspace.Path("queue.json"));

        var job = MakeJob(workspace.Path("track.wav"), JobProfile.Default, workspace.Root);
        job.Status = JobStatus.Completed;

        store.Save(new[] { job });

        Assert.Equal(JobStatus.Completed, store.Load().Single().Status);
    }

    [Fact]
    public void MissingQueueFileYieldsAnEmptyQueue()
    {
        using var workspace = new TempWorkspace();
        var store = new QueueStore(workspace.Path("does-not-exist.json"));

        Assert.Empty(store.Load());
    }

    [Fact]
    public void ACorruptQueueIsPreservedRatherThanDiscarded()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Path("queue.json");
        File.WriteAllText(path, "{ this is not json");

        var store = new QueueStore(path);
        Assert.Empty(store.Load());

        // The damaged file is moved aside instead of being silently lost.
        var salvaged = Directory.GetFiles(workspace.Root, "queue.json.corrupt-*");
        Assert.Single(salvaged);
    }

    [Fact]
    public void FlagsEnumSurvivesJsonRoundTrip()
    {
        using var workspace = new TempWorkspace();
        var store = new QueueStore(workspace.Path("queue.json"));

        var job = MakeJob(workspace.Path("track.wav"), new JobProfile(Stems.Vocals | Stems.Drums, ProcessingMode.HighSpeed), workspace.Root);
        store.Save(new[] { job });

        var round = store.Load().Single();
        Assert.Equal(Stems.Vocals | Stems.Drums, round.Stems);
    }

    /// <summary>
    /// The measurement behind the comments in <see cref="ErrorKind"/>: a queue file names failures,
    /// it does not number them. The retired member is used on purpose - it is the one the comments
    /// claim must stay present, and this is the claim that has to be true rather than asserted.
    /// </summary>
    [Fact]
    public void AFailureCauseIsWrittenByNameRatherThanByPosition()
    {
        using var workspace = new TempWorkspace();
        var store = new QueueStore(workspace.Path("queue.json"));

        var job = MakeJob(workspace.Path("track.wav"), JobProfile.Default, workspace.Root);
        job.Status = JobStatus.Failed;
        job.ErrorKind = ErrorKind.LiveAlreadyRunningWithUserProject;
        store.Save(new[] { job });

        var raw = File.ReadAllText(store.FilePath);
        const string key = "\"ErrorKind\": ";
        var at = raw.IndexOf(key, StringComparison.Ordinal);
        Assert.True(at >= 0, "the queue file carries no ErrorKind at all: " + raw);

        // The value opens with a quote, so it is a name. A number here would mean the ordinal is
        // the durable thing, and EngineSeamTests' name guard would be protecting the wrong set.
        Assert.Equal("\"", raw.Substring(at + key.Length, 1));
        Assert.Contains("\"LiveAlreadyRunningWithUserProject\"", raw);

        Assert.Equal(ErrorKind.LiveAlreadyRunningWithUserProject, store.Load().Single().ErrorKind);
    }

    /// <summary>
    /// The other half: a name this build does not have is not forgiven, so deleting a retired
    /// member really does strand the files that name it. The damaged queue is moved aside, never
    /// rewritten.
    /// </summary>
    [Fact]
    public void AQueueNamingAFailureThisBuildDoesNotHaveCannotBeLoaded()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Path("queue.json");
        File.WriteAllText(path,
            "{ \"SchemaVersion\": 1, \"Jobs\": [ { \"Id\": \"job-1\", \"ErrorKind\": \"SomeCauseNeverShipped\" } ] }");

        var store = new QueueStore(path);
        Assert.Empty(store.Load());
        Assert.Single(Directory.GetFiles(workspace.Root, "queue.json.corrupt-*"));
    }
}

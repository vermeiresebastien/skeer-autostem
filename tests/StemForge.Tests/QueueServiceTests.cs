using StemForge.Core.Models;
using StemForge.Core.Orchestration;
using StemForge.Core.Queue;
using Xunit;

namespace StemForge.Tests;

public sealed class QueueServiceTests
{
    [Fact]
    public void AddingTheSameFileTwiceOnlyQueuesItOnce()
    {
        using var workspace = new TempWorkspace();
        var service = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var file = workspace.CreateWav("track.wav");
        var destination = workspace.NewFolder("out");

        Assert.Equal(1, service.AddPaths(new[] { file }, recursive: false, JobProfile.Default, destination));
        Assert.Equal(0, service.AddPaths(new[] { file }, recursive: false, JobProfile.Default, destination));
        Assert.Single(service.Jobs);
    }

    [Fact]
    public void FolderImportIsTopLevelOnlyUnlessRecursionIsRequested()
    {
        using var workspace = new TempWorkspace();
        var service = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var folder = workspace.NewFolder("music");
        var nested = Path.Combine(folder, "nested");
        Directory.CreateDirectory(nested);

        workspace.CreateWav(Path.Combine("music", "a.wav"));
        workspace.CreateWav(Path.Combine("music", "nested", "b.wav"));
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "ignored");

        var destination = workspace.NewFolder("out");

        Assert.Equal(1, service.AddPaths(new[] { folder }, recursive: false, JobProfile.Default, destination));

        var recursive = new QueueService(new QueueStore(workspace.Path("queue2.json")));
        Assert.Equal(2, recursive.AddPaths(new[] { folder }, recursive: true, JobProfile.Default, destination));
    }

    [Fact]
    public void UnsupportedFilesAreNotQueuedByFolderImport()
    {
        using var workspace = new TempWorkspace();
        var service = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var folder = workspace.NewFolder("music");
        File.WriteAllText(Path.Combine(folder, "readme.txt"), "not audio");

        Assert.Equal(0, service.AddPaths(new[] { folder }, recursive: true, JobProfile.Default, workspace.NewFolder("out")));
    }

    [Fact]
    public void ClearCompletedKeepsUnfinishedWork()
    {
        using var workspace = new TempWorkspace();
        var store = new QueueStore(workspace.Path("queue.json"));
        var service = new QueueService(store);
        var destination = workspace.NewFolder("out");

        service.AddPaths(new[] { workspace.CreateWav("a.wav"), workspace.CreateWav("b.wav") }, false, JobProfile.Default, destination);
        service.Jobs[0].Status = JobStatus.Completed;
        service.Jobs[1].Status = JobStatus.Failed;

        Assert.Equal(1, service.ClearCompleted());
        Assert.Single(service.Jobs);
        Assert.Equal(JobStatus.Failed, service.Jobs[0].Status);
    }

    [Fact]
    public void RetryFailedReturnsJobsToTheQueue()
    {
        using var workspace = new TempWorkspace();
        var service = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var destination = workspace.NewFolder("out");

        service.AddPaths(new[] { workspace.CreateWav("a.wav") }, false, JobProfile.Default, destination);
        service.Jobs[0].Fail(ErrorKind.LiveCrashed, "boom");

        Assert.Equal(1, service.RetryFailed());
        Assert.Equal(JobStatus.Pending, service.Jobs[0].Status);
        Assert.Null(service.Jobs[0].Error);
    }

    [Fact]
    public void FreezingSettingsReKeysJobsAndMergesDuplicates()
    {
        using var workspace = new TempWorkspace();
        var service = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var destination = workspace.NewFolder("out");
        var file = workspace.CreateWav("track.wav");

        service.AddPaths(new[] { file }, false, new JobProfile(Stems.All, ProcessingMode.HighSpeed), destination);
        service.AddPaths(new[] { file }, false, new JobProfile(Stems.Drums, ProcessingMode.HighQuality), destination);
        Assert.Equal(2, service.Jobs.Count);

        // Pressing Run with one uniform profile collapses them into a single job.
        service.FreezeSettings(new JobProfile(Stems.All, ProcessingMode.HighSpeed), destination);

        Assert.Single(service.Jobs);
        Assert.Equal(Stems.All, service.Jobs[0].Stems);
        Assert.Equal(ProcessingMode.HighSpeed, service.Jobs[0].Mode);
    }

    [Fact]
    public void FreezingSettingsLeavesFinishedJobsAlone()
    {
        using var workspace = new TempWorkspace();
        var service = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var destination = workspace.NewFolder("out");

        service.AddPaths(new[] { workspace.CreateWav("done.wav") }, false, new JobProfile(Stems.Drums, ProcessingMode.HighQuality), destination);
        var completed = service.Jobs[0];
        completed.Status = JobStatus.Completed;
        var idBefore = completed.Id;

        service.AddPaths(new[] { workspace.CreateWav("todo.wav") }, false, new JobProfile(Stems.Drums, ProcessingMode.HighQuality), destination);
        service.FreezeSettings(new JobProfile(Stems.All, ProcessingMode.HighSpeed), destination);

        var stillDone = service.Jobs.Single(j => j.Id == idBefore);
        Assert.Equal(Stems.Drums, stillDone.Stems);
        Assert.Equal(ProcessingMode.HighQuality, stillDone.Mode);
    }
}

using StemForge.Core.Audio;
using StemForge.Core.Live;
using StemForge.Core.Models;
using StemForge.Core.Publish;
using StemForge.Core.Verify;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// Partial output: what a failed attempt may keep. It must never be mistaken for a
/// finished result, and only files that actually verified may be kept.
/// </summary>
public sealed class PartialOutputTests
{
    [Fact]
    public void OnlyVerifiedStemsAreKeptAndTheFolderIsMarkedPartial()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var info = WavReader.ReadInfo(source);
        var job = OutputPublisherTests.BuildJob(source, JobProfile.Default, destination);

        var generated = new Dictionary<Stems, string>
        {
            [Stems.Drums] = workspace.CreateWav("drums.wav", seconds: 2.0),   // verifies
            [Stems.Bass] = workspace.CreateWav("bass.wav", seconds: 9.0),     // wrong length
        };

        var result = OutputPublisher.PublishPartial(
            job, generated, info, VerificationOptions.Default, "12.3.5", "Live stopped unexpectedly");

        Assert.True(result.Ok, result.Error);
        Assert.True(File.Exists(Path.Combine(result.ResultFolder!, "drums.wav")));
        Assert.False(File.Exists(Path.Combine(result.ResultFolder!, "bass.wav")));
        Assert.Equal(new[] { "drums.wav" }, job.PartialFiles);

        var manifest = ManifestStore.Read(result.ResultFolder!);
        Assert.NotNull(manifest);
        Assert.True(manifest!.Partial);
        Assert.False(manifest.Complete);
    }

    [Fact]
    public void PartialOutputNeverSatisfiesTheJob()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var info = WavReader.ReadInfo(source);
        var job = OutputPublisherTests.BuildJob(source, JobProfile.Default, destination);

        OutputPublisher.PublishPartial(job, OutputPublisherTests.BuildStems(workspace, Stems.All, 2.0),
            info, VerificationOptions.Default, null, "failed");

        Assert.NotEmpty(job.PartialFiles);
        Assert.False(OutputPublisher.IsAlreadySatisfied(job, out _));
    }

    [Fact]
    public void APartialFolderIsNotReusedByALaterCompleteRun()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var info = WavReader.ReadInfo(source);
        var job = OutputPublisherTests.BuildJob(source, JobProfile.Default, destination);

        var partial = OutputPublisher.PublishPartial(job, OutputPublisherTests.BuildStems(workspace, Stems.Drums, 2.0),
            info, VerificationOptions.Default, null, "failed");
        Assert.True(partial.Ok, partial.Error);

        var complete = OutputPublisher.Publish(job, OutputPublisherTests.BuildStems(workspace, Stems.All, 2.0),
            info, VerificationOptions.Default, null);
        Assert.True(complete.Ok, complete.Error);

        Assert.NotEqual(partial.ResultFolder, complete.ResultFolder);
        Assert.True(ManifestStore.Read(partial.ResultFolder!)!.Partial);
        Assert.True(ManifestStore.Read(complete.ResultFolder!)!.Complete);
    }

    [Fact]
    public void NothingIsKeptWhenNoStemVerifies()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var info = WavReader.ReadInfo(source);
        var job = OutputPublisherTests.BuildJob(source, JobProfile.Default, destination);

        var generated = new Dictionary<Stems, string>
        {
            [Stems.Drums] = workspace.CreateWav("drums.wav", seconds: 11.0),
        };

        var result = OutputPublisher.PublishPartial(job, generated, info, VerificationOptions.Default, null, "failed");

        Assert.False(result.Ok);
        Assert.Empty(job.PartialFiles);
        Assert.Null(job.PartialFolder);
    }

    [Fact]
    public async Task AnUnsupportedExtensionIsReportedPlainly()
    {
        using var workspace = new TempWorkspace();
        var unsupported = workspace.Path("song.xyz");
        File.WriteAllText(unsupported, "not audio");

        var destination = workspace.NewFolder("out");
        var queue = new StemForge.Core.Orchestration.QueueService(
            new StemForge.Core.Queue.QueueStore(workspace.Path("queue.json")));
        queue.AddPaths(new[] { unsupported }, false, JobProfile.Default, destination);

        var log = new StemForge.Core.Logging.RunLog(workspace.Path("run.log"));
        var context = new StemForge.Core.Orchestration.BatchContext
        {
            Settings = new AppSettings { KeepAwakeDuringRun = false },
            Install = new LiveInstall("simulated", "0.0", "Simulated"),
            BaseTemplatePath = workspace.Path("unused.als"),
            Log = log,
            PersistQueue = queue.Save,
        };

        await new StemForge.Core.Orchestration.BatchRunner(
                new StemForge.Core.Live.SimulatedLiveRunner(log))
            .RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        var job = queue.Jobs.Single();
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("Unsupported file type: .xyz.", job.Error);
    }
}

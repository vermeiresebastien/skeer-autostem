using StemForge.Core.Audio;
using StemForge.Core.Models;
using StemForge.Core.Publish;
using StemForge.Core.Verify;
using Xunit;

namespace StemForge.Tests;

public sealed class OutputPublisherTests
{
    [Fact]
    public void PublishingCreatesAFolderWithTheRequestedStemNamesAndAManifest()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Artist - Track.wav", seconds: 2.0);
        var job = BuildJob(source, new JobProfile(Stems.All, ProcessingMode.HighSpeed), destination);

        var generated = BuildStems(workspace, Stems.All, 2.0);
        var result = OutputPublisher.Publish(job, generated, WavReader.ReadInfo(source), VerificationOptions.Default, "12.3.5");

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Artist - Track", Path.GetFileName(result.ResultFolder));

        foreach (var name in new[] { "vocals.wav", "drums.wav", "bass.wav", "other.wav" })
            Assert.True(File.Exists(Path.Combine(result.ResultFolder!, name)), name);

        var manifest = ManifestStore.Read(result.ResultFolder!);
        Assert.NotNull(manifest);
        Assert.True(manifest!.Complete);
        Assert.Equal(job.Stems, manifest.Stems);
        Assert.Equal(job.Mode, manifest.Mode);
        Assert.Equal(4, manifest.Files.Count);
    }

    [Fact]
    public void ADrumsOnlyJobPublishesOnlyDrums()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var job = BuildJob(source, new JobProfile(Stems.Drums, ProcessingMode.HighSpeed), destination);

        var generated = BuildStems(workspace, Stems.All, 2.0);   // Live may create more than requested
        var result = OutputPublisher.Publish(job, generated, WavReader.ReadInfo(source), VerificationOptions.Default, null);

        Assert.True(result.Ok, result.Error);
        var files = Directory.GetFiles(result.ResultFolder!, "*.wav").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "drums.wav" }, files);
    }

    [Fact]
    public void PublishingIsIdempotentForTheSameJob()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var job = BuildJob(source, JobProfile.Default, destination);
        var generated = BuildStems(workspace, Stems.All, 2.0);
        var info = WavReader.ReadInfo(source);

        var first = OutputPublisher.Publish(job, generated, info, VerificationOptions.Default, null);
        var second = OutputPublisher.Publish(job, generated, info, VerificationOptions.Default, null);

        Assert.True(first.Ok && second.Ok);
        Assert.Equal(Path.GetFileName(first.ResultFolder), Path.GetFileName(second.ResultFolder));
        Assert.Single(Directory.GetDirectories(destination), d => !Path.GetFileName(d).StartsWith('.'));
    }

    [Fact]
    public void APublishedJobIsRecognisedAsAlreadySatisfied()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var job = BuildJob(source, JobProfile.Default, destination);

        var result = OutputPublisher.Publish(job, BuildStems(workspace, Stems.All, 2.0), WavReader.ReadInfo(source), VerificationOptions.Default, null);
        Assert.True(result.Ok, result.Error);

        Assert.True(OutputPublisher.IsAlreadySatisfied(job, out var folder));
        Assert.Equal(result.ResultFolder, folder);
    }

    [Fact]
    public void AnIncompleteManifestIsNotTreatedAsSatisfied_AndIsRepairedOnRetry()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var job = BuildJob(source, JobProfile.Default, destination);
        var info = WavReader.ReadInfo(source);

        var result = OutputPublisher.Publish(job, BuildStems(workspace, Stems.All, 2.0), info, VerificationOptions.Default, null);
        Assert.True(result.Ok, result.Error);

        // Simulate losing the commit step: manifest present but not complete.
        var manifest = ManifestStore.Read(result.ResultFolder!)!;
        manifest.Complete = false;
        ManifestStore.Write(result.ResultFolder!, manifest);

        Assert.False(OutputPublisher.IsAlreadySatisfied(job, out _));

        var retry = OutputPublisher.Publish(job, BuildStems(workspace, Stems.All, 2.0), info, VerificationOptions.Default, null);
        Assert.True(retry.Ok, retry.Error);
        Assert.Equal(Path.GetFileName(result.ResultFolder), Path.GetFileName(retry.ResultFolder));
        Assert.True(ManifestStore.Read(retry.ResultFolder!)!.Complete);
    }

    [Fact]
    public void AMissingStemFailsVerificationBeforeAnythingIsPublished()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var job = BuildJob(source, JobProfile.Default, destination);

        var generated = new Dictionary<Stems, string>
        {
            [Stems.Vocals] = workspace.CreateWav("vocals.wav", seconds: 2.0),
        };

        var result = OutputPublisher.Publish(job, generated, WavReader.ReadInfo(source), VerificationOptions.Default, null);

        Assert.False(result.Ok);
        Assert.Equal(ErrorKind.StemsUnverified, result.Kind);
        Assert.DoesNotContain(Directory.GetDirectories(destination), d => !Path.GetFileName(d).StartsWith('.'));
    }

    [Fact]
    public void AStemWithTheWrongLengthIsRejected()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 20.0);
        var job = BuildJob(source, new JobProfile(Stems.Drums, ProcessingMode.HighSpeed), destination);

        var generated = new Dictionary<Stems, string>
        {
            [Stems.Drums] = workspace.CreateWav("drums.wav", seconds: 3.0),
        };

        var result = OutputPublisher.Publish(job, generated, WavReader.ReadInfo(source), VerificationOptions.Default, null);

        Assert.False(result.Ok);
        Assert.Equal(ErrorKind.StemsUnverified, result.Kind);
    }

    [Fact]
    public void AnUnwritableDestinationProducesAnActionableError()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);

        // Point the destination at a file: creating the folder must fail.
        var notAFolder = workspace.Path("blocked");
        File.WriteAllText(notAFolder, "x");

        var job = BuildJob(source, JobProfile.Default, notAFolder);
        var result = OutputPublisher.Publish(job, BuildStems(workspace, Stems.All, 2.0),
            WavReader.ReadInfo(source), VerificationOptions.Default, null);

        Assert.False(result.Ok);
        Assert.Equal(ErrorKind.DestinationUnwritable, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void TheSourceFileIsNeverModified()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = workspace.CreateWav("Track.wav", seconds: 2.0);
        var before = SourceIdentity.Capture(source);
        var job = BuildJob(source, JobProfile.Default, destination);

        OutputPublisher.Publish(job, BuildStems(workspace, Stems.All, 2.0), WavReader.ReadInfo(source), VerificationOptions.Default, null);

        Assert.False(before.HasChanged(out _));
    }

    internal static Job BuildJob(string sourcePath, JobProfile profile, string destination)
    {
        var identity = SourceIdentity.Capture(sourcePath);
        return new Job
        {
            Id = JobId.Create(identity, profile, destination),
            SourcePath = identity.Path,
            SourceSizeBytes = identity.SizeBytes,
            SourceLastWriteUtcTicks = identity.LastWriteUtcTicks,
            SourceQuickHash = identity.QuickHash,
            Stems = profile.Stems,
            Mode = profile.Mode,
            DestinationRoot = destination,
        };
    }

    internal static Dictionary<Stems, string> BuildStems(TempWorkspace workspace, Stems stems, double seconds)
    {
        var result = new Dictionary<Stems, string>();
        foreach (var stem in stems.Enumerate())
            result[stem] = workspace.CreateWav($"generated-{stem.LiveKeyword()}.wav", seconds);
        return result;
    }
}

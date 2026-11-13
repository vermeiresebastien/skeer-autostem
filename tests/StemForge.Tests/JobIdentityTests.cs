using StemForge.Core.Models;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// Job identity is what makes resume, change detection and "never reuse the wrong
/// result" work, so it gets its own tests.
/// </summary>
public sealed class JobIdentityTests
{
    [Fact]
    public void SameWorkProducesTheSameId()
    {
        using var workspace = new TempWorkspace();
        var source = SourceIdentity.Capture(workspace.CreateWav("track.wav"));
        var profile = new JobProfile(Stems.All, ProcessingMode.HighSpeed);
        var destination = workspace.NewFolder("out");

        Assert.Equal(JobId.Create(source, profile, destination), JobId.Create(source, profile, destination));
    }

    [Fact]
    public void ChangingTheSourceProducesANewId()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.CreateWav("track.wav", seconds: 2.0);
        var profile = new JobProfile(Stems.All, ProcessingMode.HighSpeed);
        var destination = workspace.NewFolder("out");

        var first = JobId.Create(SourceIdentity.Capture(path), profile, destination);

        // Rewrite the file with different content and a different length.
        workspace.CreateWav("track.wav", seconds: 3.0);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var second = JobId.Create(SourceIdentity.Capture(path), profile, destination);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(Stems.All, ProcessingMode.HighQuality)]
    [InlineData(Stems.Drums, ProcessingMode.HighSpeed)]
    [InlineData(Stems.Drums, ProcessingMode.HighQuality)]
    public void DrumsOnlyHighQualityNeverMatchesAllStemsHighSpeed(Stems stems, ProcessingMode mode)
    {
        using var workspace = new TempWorkspace();
        var source = SourceIdentity.Capture(workspace.CreateWav("track.wav"));
        var destination = workspace.NewFolder("out");

        var baseline = JobId.Create(source, new JobProfile(Stems.All, ProcessingMode.HighSpeed), destination);
        var other = JobId.Create(source, new JobProfile(stems, mode), destination);

        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ADifferentDestinationIsADifferentJob()
    {
        using var workspace = new TempWorkspace();
        var source = SourceIdentity.Capture(workspace.CreateWav("track.wav"));
        var profile = new JobProfile(Stems.All, ProcessingMode.HighSpeed);

        var a = JobId.Create(source, profile, workspace.NewFolder("out-a"));
        var b = JobId.Create(source, profile, workspace.NewFolder("out-b"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SourceIdentityDetectsAChangedFile()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.CreateWav("track.wav", seconds: 2.0);
        var identity = SourceIdentity.Capture(path);

        Assert.False(identity.HasChanged(out _));

        workspace.CreateWav("track.wav", seconds: 4.0);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(2));

        Assert.True(identity.HasChanged(out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}

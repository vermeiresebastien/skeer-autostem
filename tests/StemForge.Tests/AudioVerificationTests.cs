using StemForge.Core.Audio;
using StemForge.Core.Models;
using StemForge.Core.Verify;
using Xunit;

namespace StemForge.Tests;

public sealed class AudioVerificationTests
{
    [Fact]
    public void WavRoundTripsThroughTheReader()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.CreateWav("tone.wav", seconds: 1.5, sampleRate: 48000, channels: 2);

        var info = WavReader.ReadInfo(path);

        Assert.Equal(WavSampleFormat.Float, info.Format);
        Assert.Equal(32, info.BitsPerSample);
        Assert.Equal(2, info.Channels);
        Assert.Equal(48000, info.SampleRate);
        Assert.Equal(1.5, info.DurationSeconds, 3);
    }

    [Fact]
    public void AiffRoundTripsAndConvertsToTheRequestedWav()
    {
        using var workspace = new TempWorkspace();
        var aiff = workspace.Path("stem.aif");
        var samples = new float[44100 * 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = (float)(0.3 * Math.Sin(i * 0.01));

        AiffWriter.WriteFloat32(aiff, samples, 2, 44100);

        var info = AiffReader.ReadInfo(aiff);
        Assert.Equal(1.0, info.DurationSeconds, 3);
        Assert.Equal(2, info.Channels);

        var converted = StemForge.Core.Live.StemFormatConverter.EnsureWav(aiff);
        Assert.True(converted.Converted);
        Assert.EndsWith(".wav", converted.Path);

        var convertedInfo = WavReader.ReadInfo(converted.Path);
        Assert.Equal(1.0, convertedInfo.DurationSeconds, 3);
    }

    [Fact]
    public void AlreadyWavStemsAreNotConverted()
    {
        using var workspace = new TempWorkspace();
        var wav = workspace.CreateWav("drums.wav");

        var result = StemForge.Core.Live.StemFormatConverter.EnsureWav(wav);

        Assert.False(result.Converted);
        Assert.Equal(wav, result.Path);
    }

    [Fact]
    public void VerificationPassesForMatchingStems()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateWav("source.wav", seconds: 3.0);
        var sourceInfo = WavReader.ReadInfo(source);

        var files = new Dictionary<Stems, string>
        {
            [Stems.Vocals] = workspace.CreateWav("vocals.wav", seconds: 3.0),
            [Stems.Drums] = workspace.CreateWav("drums.wav", seconds: 3.0),
            [Stems.Bass] = workspace.CreateWav("bass.wav", seconds: 3.0),
            [Stems.Others] = workspace.CreateWav("other.wav", seconds: 3.0),
        };

        var report = StemVerifier.Verify(Stems.All, files, sourceInfo, VerificationOptions.Default);

        Assert.True(report.Ok, report.Summary);
        Assert.Equal(4, report.Files.Count);
        Assert.All(report.Files, f => Assert.True(f.Ok));
    }

    [Fact]
    public void VerificationFailsWhenADurationIsOff()
    {
        using var workspace = new TempWorkspace();
        var sourceInfo = WavReader.ReadInfo(workspace.CreateWav("source.wav", seconds: 10.0));

        var files = new Dictionary<Stems, string>
        {
            [Stems.Drums] = workspace.CreateWav("drums.wav", seconds: 4.0),
        };

        var report = StemVerifier.Verify(Stems.Drums, files, sourceInfo, VerificationOptions.Default);

        Assert.False(report.Ok);
        Assert.Contains("drums", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerificationFailsWhenAStemIsMissing()
    {
        using var workspace = new TempWorkspace();
        var sourceInfo = WavReader.ReadInfo(workspace.CreateWav("source.wav", seconds: 3.0));

        var files = new Dictionary<Stems, string>
        {
            [Stems.Drums] = workspace.CreateWav("drums.wav", seconds: 3.0),
        };

        var report = StemVerifier.Verify(Stems.All, files, sourceInfo, VerificationOptions.Default);

        Assert.False(report.Ok);
        Assert.Contains("no file was produced", report.Summary);
    }

    [Fact]
    public void VerificationFailsWhenNotWav()
    {
        using var workspace = new TempWorkspace();
        var sourceInfo = WavReader.ReadInfo(workspace.CreateWav("source.wav", seconds: 3.0));
        var notWav = workspace.Path("drums.wav");
        File.WriteAllBytes(notWav, new byte[5000]);

        var report = StemVerifier.Verify(Stems.Drums, new Dictionary<Stems, string> { [Stems.Drums] = notWav }, sourceInfo, VerificationOptions.Default);

        Assert.False(report.Ok);
    }

    [Fact]
    public void TheDocumentedToleranceScalesWithTrackLength()
    {
        var options = VerificationOptions.Default;

        // 0.5% of the source length, but never below the 50 ms floor.
        Assert.Equal(0.05, options.ToleranceSeconds(4.0), 3);
        Assert.Equal(0.5, options.ToleranceSeconds(100.0), 3);
    }
}

using StemForge.Core.Audio;
using StemForge.Core.Live;
using StemForge.Core.Logging;
using StemForge.Core.Models;
using StemForge.Core.Orchestration;
using StemForge.Core.Queue;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// Covers the MP3 container reader and the queue path that depends on it: an MP3 is measured,
/// then handed to the engine in place, so nothing here decodes audio.
/// </summary>
public sealed class Mp3ReaderTests
{
    private const long FramesInTwoSeconds = 77;

    [Fact]
    public void AnMp3ReportsItsRateChannelsAndFrameCount()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.CreateMp3("track.mp3", seconds: 2.0);

        var info = Mp3Reader.ReadInfo(path);

        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(2, info.Channels);
        Assert.Equal(FramesInTwoSeconds * 1152, info.SampleFrames);
        Assert.True(Math.Abs(info.DurationSeconds - 2.0) < 0.03, $"duration was {info.DurationSeconds:F3}s");
    }

    [Fact]
    public void ALeadingId3TagIsNotCountedAsAudio()
    {
        using var workspace = new TempWorkspace();
        var plain = workspace.CreateMp3("plain.mp3", seconds: 2.0);
        var tagged = workspace.CreateMp3("tagged.mp3", seconds: 2.0, id3TagBytes: 256);

        Assert.Equal(Mp3Reader.ReadInfo(plain).SampleFrames, Mp3Reader.ReadInfo(tagged).SampleFrames);
    }

    [Fact]
    public void AVariableBitrateEncodeIsCountedByFramesNotBytes()
    {
        using var workspace = new TempWorkspace();
        var cbr = workspace.CreateMp3("cbr.mp3", seconds: 2.0);
        var vbr = workspace.CreateMp3("vbr.mp3", seconds: 2.0, bitrates: new[] { 320, 96, 128 });

        Assert.True(new FileInfo(vbr).Length > new FileInfo(cbr).Length);
        Assert.Equal(Mp3Reader.ReadInfo(cbr).SampleFrames, Mp3Reader.ReadInfo(vbr).SampleFrames);
    }

    [Fact]
    public void AMonoEncodeReportsOneChannel()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.CreateMp3("mono.mp3", seconds: 1.0, channels: 1);

        var info = Mp3Reader.ReadInfo(path);

        Assert.Equal(1, info.Channels);
        Assert.Equal(39L * 1152, info.SampleFrames);
    }

    [Fact]
    public void TheSharedReaderAcceptsMp3AlongsideWavAndAiff()
    {
        using var workspace = new TempWorkspace();
        var mp3 = workspace.CreateMp3("track.mp3", seconds: 2.0);
        var wav = workspace.CreateWav("track.wav", seconds: 2.0);

        Assert.Equal(44100, AudioFileReader.ReadInfo(mp3).SampleRate);
        Assert.True(AudioFileReader.IsReadable(mp3));
        Assert.False(AudioFileReader.HasDecodableSamples(mp3));
        Assert.True(AudioFileReader.HasDecodableSamples(wav));
    }

    [Fact]
    public void AFileThatIsNotAudioIsRejected()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.Path("fake.m4a");
        File.WriteAllText(path, "This is not an audio file, it is a sentence with a few words in it.");

        Assert.False(Mp3Reader.IsMp3(path));
        Assert.Throws<InvalidDataException>(() => AudioFileReader.ReadInfo(path));
    }

    [Fact]
    public void AContainerIsJudgedByItsBytesNotItsName()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.CreateWav("mislabelled.mp3", seconds: 2.0);

        var info = AudioFileReader.ReadInfo(path);

        Assert.Equal(WavSampleFormat.Float, info.Format);
        Assert.Equal(32, info.BitsPerSample);
    }

    [Fact]
    public async Task TheSimulatedEngineSeparatesAnMp3()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateMp3("song.mp3", seconds: 2.0);
        var queue = new QueueService(new QueueStore(workspace.Path("queue.json")));
        var destination = workspace.NewFolder("out");
        queue.AddPaths(new[] { source }, recursive: false, JobProfile.Default, destination);

        var log = new RunLog(workspace.Path("run.log"));
        var context = new BatchContext
        {
            Settings = new AppSettings { KeepAwakeDuringRun = false, RecursiveImport = false },
            Install = new LiveInstall("simulated", "0.0", "Simulated"),
            BaseTemplatePath = workspace.Path("unused.als"),
            Log = log,
            PersistQueue = queue.Save,
        };

        var summary = await new BatchRunner(new SimulatedLiveRunner(log))
            .RunAsync(queue.Jobs.ToList(), context, CancellationToken.None);

        Assert.Equal(1, summary.Completed);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(JobStatus.Completed, queue.Jobs.Single().Status);
        Assert.True(File.Exists(Path.Combine(destination, "song", "vocals.wav")));
    }
}

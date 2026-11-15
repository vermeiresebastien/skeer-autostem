using StemForge.Core.Audio;
using StemForge.Core.Support;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// Creates an isolated data root for a test and a small real WAV to work with.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    private readonly string _previousRoot;

    public TempWorkspace()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StemForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        _previousRoot = Environment.GetEnvironmentVariable("STEMFORGE_DATA_ROOT") ?? string.Empty;
        Environment.SetEnvironmentVariable("STEMFORGE_DATA_ROOT", System.IO.Path.Combine(Root, "appdata"));
    }

    public string Root { get; }

    public string Path(string name) => System.IO.Path.Combine(Root, name);

    public string NewFolder(string name)
    {
        var folder = Path(name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Writes a real, readable WAV of the requested length.</summary>
    public string CreateWav(string name, double seconds = 2.0, int sampleRate = 44100, int channels = 2, double frequency = 220.0)
    {
        var path = Path(name);
        var frames = (int)(seconds * sampleRate);
        var samples = new float[frames * channels];

        for (var frame = 0; frame < frames; frame++)
        {
            var value = (float)(0.4 * Math.Sin(2 * Math.PI * frequency * frame / sampleRate));
            for (var ch = 0; ch < channels; ch++) samples[frame * channels + ch] = value;
        }

        WavWriter.WriteFloat32(path, samples, channels, sampleRate);
        return path;
    }

    public string CreateSilentWav(string name, double seconds = 2.0, int sampleRate = 44100, int channels = 2)
    {
        var path = Path(name);
        var frames = (int)(seconds * sampleRate);
        WavWriter.WriteFloat32(path, new float[frames * channels], channels, sampleRate);
        return path;
    }

    /// <summary>
    /// Writes a structurally valid MPEG-1 Layer III file: real frame headers over silent
    /// bodies, optionally behind an ID3v2 tag. Several bitrates interleave them, which is how
    /// a variable bitrate encode looks to a reader that counts frames instead of decoding them.
    /// </summary>
    public string CreateMp3(string name, double seconds = 2.0, int sampleRate = 44100, int channels = 2,
        int id3TagBytes = 0, int[]? bitrates = null)
    {
        var path = Path(name);
        var frames = (int)Math.Ceiling(seconds * sampleRate / 1152.0);
        var rateIndex = sampleRate switch
        {
            44100 => 0,
            48000 => 1,
            32000 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Not an MPEG-1 Layer III rate."),
        };

        using var fs = File.Create(path);
        if (id3TagBytes > 0) WriteId3Tag(fs, id3TagBytes);

        var rates = bitrates ?? DefaultBitrates;

        for (var frame = 0; frame < frames; frame++)
        {
            var bitrate = rates[frame % rates.Length];
            var bitrateIndex = Array.IndexOf(BitrateTable, bitrate) + 1;
            if (bitrateIndex < 1) throw new ArgumentOutOfRangeException(nameof(bitrates), bitrate, "Not an MPEG-1 Layer III bitrate.");

            var frameLength = 144 * bitrate * 1000 / sampleRate;

            fs.WriteByte(0xFF);
            fs.WriteByte(0xFB);
            fs.WriteByte((byte)(bitrateIndex << 4 | rateIndex << 2));
            fs.WriteByte((byte)(channels == 1 ? 0xC0 : 0x00));
            fs.Write(new byte[frameLength - 4]);
        }

        return path;
    }

    private static readonly int[] BitrateTable = { 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
    private static readonly int[] DefaultBitrates = { 128 };

    /// <summary>An ID3v2 header whose size field is synchsafe, followed by that many bytes.</summary>
    private static void WriteId3Tag(FileStream fs, int size)
    {
        fs.Write("ID3"u8);
        fs.WriteByte(4);
        fs.WriteByte(0);
        fs.WriteByte(0);
        fs.WriteByte((byte)(size >> 21 & 0x7F));
        fs.WriteByte((byte)(size >> 14 & 0x7F));
        fs.WriteByte((byte)(size >> 7 & 0x7F));
        fs.WriteByte((byte)(size & 0x7F));
        fs.Write(new byte[size]);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("STEMFORGE_DATA_ROOT",
            string.IsNullOrEmpty(_previousRoot) ? null : _previousRoot);

        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}

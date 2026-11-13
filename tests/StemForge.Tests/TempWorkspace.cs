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

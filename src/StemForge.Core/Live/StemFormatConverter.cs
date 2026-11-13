using StemForge.Core.Audio;
using StemForge.Core.Support;

namespace StemForge.Core.Live;

/// <summary>
/// Guarantees the requested WAV output no matter which format Live produced.
///
/// Ableton's stem separation writes into the project's Samples/Processed/Stems folder.
/// If the installed build writes WAV (the normal case) the file is used as-is. If a
/// supported non-WAV format appears, it is decoded and re-written as a 32-bit float WAV
/// byte-for-byte in value - no normalisation, no effects, no time stretching.
/// </summary>
public static class StemFormatConverter
{
    public static bool IsRequestedWav(string path) => WavReader.IsWav(path);

    /// <summary>
    /// Returns a path to a WAV rendition of <paramref name="source"/>, converting only
    /// when needed. Converted files are written next to the original.
    /// </summary>
    public static ConvertedStem EnsureWav(string source)
    {
        if (IsRequestedWav(source))
            return new ConvertedStem(source, Converted: false);

        if (!AiffReader.IsAiff(source))
            throw new InvalidDataException(
                $"Live produced '{Path.GetFileName(source)}', which is neither WAV nor AIFF and cannot be converted without changing the audio.");

        var info = AiffReader.ReadInfo(source);
        var samples = AiffReader.ReadSamples(source, out var confirmed);

        var target = Path.ChangeExtension(source, ".wav");
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        WavWriter.WriteFloat32(target, samples, confirmed.Channels, confirmed.SampleRate);

        return new ConvertedStem(target, Converted: true, OriginalInfo: info);
    }
}

public sealed record ConvertedStem(string Path, bool Converted, WavInfo? OriginalInfo = null);

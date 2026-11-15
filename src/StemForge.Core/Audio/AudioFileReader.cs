namespace StemForge.Core.Audio;

/// <summary>
/// Format-agnostic reading of audio sources. WAV and AIFF are read down to the samples;
/// MP3 is read as a container only, because Live decodes it in place.
/// </summary>
public static class AudioFileReader
{
    public static WavInfo ReadInfo(string path)
    {
        if (WavReader.IsWav(path)) return WavReader.ReadInfo(path);
        if (AiffReader.IsAiff(path)) return AiffReader.ReadInfo(path);
        if (Mp3Reader.IsMp3(path)) return Mp3Reader.ReadInfo(path);
        throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a readable WAV, AIFF or MP3 file.");
    }

    /// <summary>False for containers this tool only measures, so no caller pretends to decode them.</summary>
    public static bool HasDecodableSamples(string path) => WavReader.IsWav(path) || AiffReader.IsAiff(path);

    public static float[] ReadSamples(string path, out WavInfo info)
    {
        if (WavReader.IsWav(path)) return WavReader.ReadSamples(path, out info);
        if (AiffReader.IsAiff(path)) return AiffReader.ReadSamples(path, out info);
        throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a readable WAV or AIFF file.");
    }

    public static bool IsReadable(string path)
    {
        try
        {
            ReadInfo(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

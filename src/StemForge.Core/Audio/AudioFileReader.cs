namespace StemForge.Core.Audio;

/// <summary>Format-agnostic reading of WAV and AIFF sources.</summary>
public static class AudioFileReader
{
    public static WavInfo ReadInfo(string path)
    {
        if (WavReader.IsWav(path)) return WavReader.ReadInfo(path);
        if (AiffReader.IsAiff(path)) return AiffReader.ReadInfo(path);
        throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a readable WAV or AIFF file.");
    }

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

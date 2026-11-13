namespace StemForge.Core.Media;

/// <summary>Audio formats accepted as stem-separation sources.</summary>
public static class AudioFileTypes
{
    public static readonly string[] SupportedExtensions =
    {
        ".wav", ".wave", ".aif", ".aiff", ".aifc",
        ".mp3", ".m4a", ".mp4", ".flac", ".ogg",
    };

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string DisplayList => string.Join(", ", SupportedExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()));

    /// <summary>Formats we can turn into the requested WAV output without re-rendering.</summary>
    public static readonly string[] ConvertibleToWavExtensions =
    {
        ".aif", ".aiff", ".aifc", ".wav", ".wave",
    };
}

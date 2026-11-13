using System.IO;

namespace StemForge.App;

/// <summary>
/// Minimal startup trace. A desktop app that fails before its window appears leaves no
/// console output, so the first few steps are recorded to a file next to the app's data.
/// </summary>
internal static class StartupTrace
{
    private static readonly object Gate = new();

    public static string FilePath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SkeerAutoSTEM",
            "startup.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Tracing must never be the reason the app fails.
        }
    }
}

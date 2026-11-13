using System.Text;

namespace StemForge.Core.Support;

/// <summary>
/// Turns arbitrary text (track names, stem descriptors) into a folder name that is
/// always valid on Windows, and resolves duplicates so runs never overwrite each other.
/// </summary>
public static class PathSanitizer
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars()
        .Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })
        .Distinct()
        .ToArray();

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public const int MaxFolderNameLength = 120;

    public static string SanitizeFolderName(string name, string fallback = "Untitled")
    {
        if (string.IsNullOrWhiteSpace(name)) name = fallback;

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsControl(ch)) continue;
            sb.Append(Invalid.Contains(ch) ? '_' : ch);
        }

        var result = sb.ToString().Trim();

        // Windows strips trailing dots and spaces from file names.
        result = result.TrimEnd('.', ' ');

        // Collapse whitespace runs.
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ");

        if (result.Length == 0) result = fallback;

        if (ReservedNames.Contains(result)) result += "_";

        if (result.Length > MaxFolderNameLength)
        {
            result = result[..MaxFolderNameLength].TrimEnd('.', ' ');
            if (result.Length == 0) result = fallback;
        }

        return result;
    }

    /// <summary>True when two paths point at the same file/folder (case-insensitive on Windows).</summary>
    public static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\', '/'),
                Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}

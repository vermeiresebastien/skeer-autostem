namespace StemForge.Core.Live;

/// <summary>
/// An Ableton Live installation this tool can drive.
/// </summary>
public sealed record LiveInstall(string ExePath, string Version, string Edition)
{
    /// <summary>Stem separation ("Separate Stems") was added in Live 12.1.</summary>
    public static readonly Version MinimumStemSeparationVersion = new(12, 1);

    public Version? ParsedVersion =>
        System.Version.TryParse(Version, out var parsed) ? parsed : null;

    public bool SupportsStemSeparation =>
        ParsedVersion is { } v && v >= MinimumStemSeparationVersion;

    /// <summary>
    /// Both High Speed and High Quality are exposed by the same Separate Stems dialog
    /// (a single "High Quality" toggle) for every build that has stem separation.
    /// </summary>
    public bool SupportsBothProcessingModes => SupportsStemSeparation;

    public string DisplayName => $"Ableton Live {Version} {Edition}".Trim();

    /// <summary>Folder that contains the Program and Resources folders.</summary>
    public string InstallRoot
    {
        get
        {
            var programFolder = Path.GetDirectoryName(ExePath)!;   // ...\Program
            return Path.GetDirectoryName(programFolder) ?? programFolder;
        }
    }
}

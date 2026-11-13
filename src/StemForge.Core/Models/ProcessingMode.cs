namespace StemForge.Core.Models;

/// <summary>
/// Ableton Live's two stem-separation processing modes.
/// High Speed separates all stems in a single pass; High Quality uses separate
/// passes for vocals, drums and bass (and always needs the other passes when
/// "Others" is requested).
/// </summary>
public enum ProcessingMode
{
    HighSpeed,
    HighQuality,
}

public static class ProcessingModeExtensions
{
    public static string DisplayName(this ProcessingMode mode) => mode switch
    {
        ProcessingMode.HighSpeed => "High Speed",
        ProcessingMode.HighQuality => "High Quality",
        _ => mode.ToString(),
    };

    /// <summary>Suffix used to keep different modes from overwriting each other's results.</summary>
    public static string ProfileSuffix(this ProcessingMode mode) => mode switch
    {
        ProcessingMode.HighSpeed => string.Empty,
        ProcessingMode.HighQuality => "High Quality",
        _ => mode.ToString(),
    };

    /// <summary>
    /// The state the "High Quality" toggle in Live's Separate Stems dialog must be in.
    /// Live exposes a single High Quality switch; unchecked means High Speed.
    /// </summary>
    public static bool HighQualityToggle(this ProcessingMode mode) => mode == ProcessingMode.HighQuality;
}

using StemForge.Core.Support;

namespace StemForge.Core.Live;

/// <summary>
/// Tunable geometry for the Live UI, so the automation can be adjusted without
/// recompiling when a Live window layout differs (display scaling, panel sizes).
/// All fractions are relative to the window's client area, which is what makes the
/// coordinates survive window moves and different screen resolutions.
/// </summary>
public sealed class LiveUiProfile
{
    private static readonly string DefaultPath = Path.Combine(AppPaths.Root, "ui-profile.json");

    // --- Menu bar ---
    /// <summary>Fraction of client width where the "Create" menu title sits.</summary>
    public double CreateMenuXFraction { get; set; } = 0.052;

    /// <summary>Fraction of client height where the menu bar row sits.</summary>
    public double MenuBarYFraction { get; set; } = 0.038;

    // --- Clip ---
    /// <summary>Fallback click point for the only audio clip in the working set.</summary>
    public double ClipXFraction { get; set; } = 0.35;
    public double ClipYFraction { get; set; } = 0.62;

    // --- Timing ---
    public int LiveStartupTimeoutSeconds { get; set; } = 180;
    public int SetLoadTimeoutSeconds { get; set; } = 180;
    public int DialogTimeoutSeconds { get; set; } = 60;

    /// <summary>Seconds a generated file's size must stay unchanged before it counts as finished.</summary>
    public double FileStabilitySeconds { get; set; } = 2.0;

    public bool CaptureDiagnosticScreenshots { get; set; } = true;

    public static LiveUiProfile Load()
    {
        try
        {
            var profile = JsonStore.Load<LiveUiProfile>(DefaultPath);
            if (profile is not null) return profile;
        }
        catch
        {
            // fall back to defaults
        }

        var defaults = new LiveUiProfile();
        try
        {
            JsonStore.Save(DefaultPath, defaults);
        }
        catch
        {
            // read-only profile location is not fatal
        }
        return defaults;
    }
}

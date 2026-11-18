using StemForge.Core.Support;

namespace StemForge.Core.Live;

/// <summary>
/// Tunable geometry for the Live UI, so the automation can be adjusted without
/// recompiling when a Live window layout differs (display scaling, panel sizes).
///
/// All fractions are relative to the window rectangle - GetWindowRect, caption and borders
/// included - because that is the only rectangle available when Live's accessibility tree
/// is not answering. Anything the tree does report is clicked at its own rectangle instead.
/// </summary>
public sealed class LiveUiProfile
{
    private static readonly string DefaultPath = Path.Combine(AppPaths.Root, "ui-profile.json");

    // --- Menu bar ---
    /// <summary>Fraction of window width where the "Create" menu title sits.</summary>
    public double CreateMenuXFraction { get; set; } = 0.052;

    /// <summary>
    /// Fraction of window height where the menu bar row sits.
    ///
    /// Measured from Live's own UI Automation tree, not from a screenshot: the maximised window
    /// rectangle is (-8,-8,1936,1066) and the menu entries are reported at y=23..46 - "Create"
    /// itself as (67,23,48,23), so its centre is y=34 and (34 - -8) / 1066 = 0.0394. The row is
    /// Live's fixed-height chrome, so the fraction only has to survive the window frame: 0.038
    /// aims at y=32 and 0.039 at y=33, both inside it. A value of 0.055 aims at y=50, which is
    /// the transport row underneath.
    /// </summary>
    public double MenuBarYFraction { get; set; } = 0.039;

    // --- Clip ---
    /// <summary>Fallback click point for the only audio clip in the working set.</summary>
    public double ClipXFraction { get; set; } = 0.35;
    public double ClipYFraction { get; set; } = 0.62;

    // --- Timing ---
    public int LiveStartupTimeoutSeconds { get; set; } = 180;
    public int SetLoadTimeoutSeconds { get; set; } = 180;
    public int DialogTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// How long Live may be busy with its own background work - file analysis in particular,
    /// which on a large library runs into minutes - before the run stops waiting for it.
    /// </summary>
    public int SettleTimeoutSeconds { get; set; } = 900;

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

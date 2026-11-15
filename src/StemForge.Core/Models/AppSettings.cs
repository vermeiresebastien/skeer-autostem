using StemForge.Core.Engines;

namespace StemForge.Core.Models;

/// <summary>User preferences. Persisted separately from the queue.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Where the stems come from. Ableton Live is the default and, for now, the only engine
    /// that exists; the value is persisted so a machine configured for a later engine keeps it.
    /// </summary>
    public EngineKind Engine { get; set; } = EngineKind.Live;

    /// <summary>Chosen output folder, remembered between sessions.</summary>
    public string? OutputFolder { get; set; }

    /// <summary>Verified path to the Ableton Live executable.</summary>
    public string? LiveExePath { get; set; }

    /// <summary>Version string reported by the executable, e.g. "12.3.5".</summary>
    public string? LiveVersion { get; set; }

    /// <summary>Edition detected from the executable name / product info, e.g. "Suite".</summary>
    public string? LiveEdition { get; set; }

    /// <summary>Last processing mode used. High Speed is the first-launch default.</summary>
    public ProcessingMode LastMode { get; set; } = ProcessingMode.HighSpeed;

    /// <summary>Last stem selection. All four stems is the first-launch default.</summary>
    public Stems LastStems { get; set; } = Stems.All;

    public StemsPreset LastStemsPreset { get; set; } = StemsPreset.All;

    /// <summary>Whether Add Folder recurses into subfolders. Made explicit in the UI.</summary>
    public bool RecursiveImport { get; set; }

    /// <summary>Keep the machine awake while a run is active.</summary>
    public bool KeepAwakeDuringRun { get; set; } = true;

    /// <summary>Hard cap on a single job's separation stage.</summary>
    public int MaxSeparationMinutes { get; set; } = 30;

    /// <summary>Run the simulated engine instead of driving Live. Development/testing aid.</summary>
    public bool UseSimulatedRunner { get; set; }

    /// <summary>Set once the user has accepted the overnight run conditions.</summary>
    public bool HasConfirmedRunConditions { get; set; }

    /// <summary>Fraction of the source duration a stem may differ by and still verify.</summary>
    public double DurationToleranceFraction { get; set; } = 0.005;

    /// <summary>Absolute floor for the duration tolerance, in seconds.</summary>
    public double DurationToleranceFloorSeconds { get; set; } = 0.05;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

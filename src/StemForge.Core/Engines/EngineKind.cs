using StemForge.Core.Live;

namespace StemForge.Core.Engines;

/// <summary>
/// Where the stems come from. The queue, the verification and the published folder layout do
/// not care which of these is chosen; only <see cref="SeparatorFactory"/> and the runner
/// registry do.
/// </summary>
public enum EngineKind
{
    /// <summary>
    /// Ableton Live driven through UI Automation. The original engine, and the only one whose
    /// output is literally what Live's own separator produces.
    /// </summary>
    Live = 0,

    /// <summary>
    /// A model running on this machine: no Live, no exclusive desktop, real progress, immediate
    /// cancellation. Not implemented yet - see docs/SEPARATION-ENGINES.md.
    /// </summary>
    Local = 1,

    /// <summary>A hosted separation service. Not implemented and not scheduled.</summary>
    Cloud = 2,
}

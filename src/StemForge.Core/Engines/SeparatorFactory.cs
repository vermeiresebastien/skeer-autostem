using StemForge.Core.Live;
using StemForge.Core.Logging;

namespace StemForge.Core.Engines;

/// <summary>
/// The single place a setting becomes an engine. Adding an engine means adding one case here
/// and one implementation of <see cref="ISeparator"/>; the queue, the batch runner, the
/// verification and the UI are not involved.
/// </summary>
public static class SeparatorFactory
{
    /// <summary>One row of the engine list the UI can show, including the honest "not yet".</summary>
    public sealed record EngineInfo(EngineKind Kind, string Name, string Description, bool IsImplemented);

    /// <summary>
    /// Raised when a machine is asked for an engine it cannot run. The message is written for a
    /// person reading a failure row, not for a log.
    /// </summary>
    public sealed class EngineUnavailableException(string message) : Exception(message);

    public static bool IsImplemented(EngineKind kind) => kind == EngineKind.Live;

    public static IReadOnlyList<EngineInfo> Catalogue { get; } =
    [
        new(EngineKind.Live, "Ableton Live",
            "Live's own separator, driven through UI Automation. Needs Live 12.1 or newer and " +
            "the desktop to itself for the duration of the run.",
            IsImplemented(EngineKind.Live)),

        new(EngineKind.Local, "This machine",
            "A separation model running locally - no Ableton Live, no internet, real progress " +
            "and immediate cancellation. Not in this build yet.",
            IsImplemented(EngineKind.Local)),

        new(EngineKind.Cloud, "Cloud",
            "A hosted separation service. Not in this build.",
            IsImplemented(EngineKind.Cloud)),
    ];

    public static ISeparator Create(EngineKind kind, RunLog log) => kind switch
    {
        EngineKind.Live => new LiveAutomationRunner(log),
        EngineKind.Local => throw new EngineUnavailableException(
            "The local engine is not in this build. Use the Ableton Live engine, which produces " +
            "the same family of stems."),
        _ => throw new EngineUnavailableException(
            $"The {kind} engine is not in this build."),
    };

    /// <summary>
    /// The engine for a real run, or the test double when the caller asked for one. The
    /// simulated runner is deliberately not an <see cref="EngineKind"/>: it is a development
    /// aid that never claims to be Ableton verification, so it has no business in the picker.
    /// </summary>
    public static ISeparator CreateForRun(EngineKind kind, RunLog log, bool simulate) =>
        simulate ? new SimulatedLiveRunner(log) : Create(kind, log);
}

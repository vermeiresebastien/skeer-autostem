using StemForge.Core.Live;

namespace StemForge.Core.Engines;

/// <summary>
/// Produces the stems for exactly one track, by whatever means.
/// </summary>
/// <remarks>
/// This used to be called <c>ILiveRunner</c>, which was true while Ableton Live was the only
/// way to separate a track. Everything above this interface - the queue, the folder layout,
/// verification, publishing, the resume rules - is engine-independent, and an engine
/// only has to honour the request/outcome contract below to be added.
/// </remarks>
public interface ISeparator
{
    /// <summary>Name shown in the log, the status line and the engine picker.</summary>
    string Name { get; }

    /// <summary>
    /// Which family this engine belongs to. Failures are classified against it, so a local
    /// engine cannot report that Ableton Live lost the foreground.
    /// </summary>
    EngineKind Kind { get; }

    Task<SeparationOutcome> SeparateAsync(SeparationRequest request, CancellationToken cancellationToken);
}

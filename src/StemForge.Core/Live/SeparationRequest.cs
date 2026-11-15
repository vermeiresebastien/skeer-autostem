using StemForge.Core.Audio;
using StemForge.Core.Models;

namespace StemForge.Core.Live;

public sealed record SeparationProgress(JobStage Stage, string? Detail = null, double? Percent = null);

/// <summary>Everything the Live runner needs to process exactly one track.</summary>
public sealed class SeparationRequest
{
    public required Job Job { get; init; }
    public required JobProfile Profile { get; init; }
    public required LiveInstall Install { get; init; }
    public required SourceIdentity Source { get; init; }
    public required WavInfo SourceInfo { get; init; }

    /// <summary>Folder reserved for this job's working Live project and its outputs.</summary>
    public required string WorkingFolder { get; init; }

    /// <summary>Live's stock empty-audio-track template used as the structural base.</summary>
    public required string BaseTemplatePath { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);

    public IProgress<SeparationProgress>? Progress { get; init; }

    /// <summary>The Live project folder for this job, named after the set inside it.</summary>
    public string ProjectFolder => Projects.WorkingProjectBuilder.ProjectFolder(WorkingFolder);

    /// <summary>Where Live writes generated audio: &lt;project&gt;\Samples\Processed\Stems.</summary>
    public string LiveStemsFolder => Path.Combine(ProjectFolder, "Samples", "Processed", "Stems");

    public string ProjectFilePath => Path.Combine(ProjectFolder, Projects.WorkingProjectBuilder.WorkingProjectFileName);
}

public sealed record SeparationOutcome(
    bool Success,
    ErrorKind Kind,
    string? Error,
    IReadOnlyDictionary<Stems, string> GeneratedFiles,
    double SeparationSeconds,
    bool WasCancelled)
{
    public static SeparationOutcome Cancelled(ErrorKind kind, string error) =>
        new(false, kind, error, new Dictionary<Stems, string>(), 0, true);

    public static SeparationOutcome Failure(ErrorKind kind, string error, double seconds = 0) =>
        new(false, kind, error, new Dictionary<Stems, string>(), seconds, false);
}

/// <summary>
/// Serialises access to Ableton Live so two runner instances cannot drive it at once.
/// </summary>
public sealed class LiveSessionGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _owned;

    public LiveSessionGuard(string name = @"Global\SkeerAutoSTEM.LiveRunner")
    {
        _mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            _owned = _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _owned = true;
        }
    }

    public bool Acquired => _owned;

    public void Dispose()
    {
        if (_owned)
        {
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex.Dispose();
    }
}

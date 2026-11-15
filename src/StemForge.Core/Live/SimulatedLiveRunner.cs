using StemForge.Core.Audio;
using StemForge.Core.Engines;
using StemForge.Core.Logging;
using StemForge.Core.Models;

namespace StemForge.Core.Live;

/// <summary>
/// A stand-in for Ableton Live used for end-to-end testing and for machines where a real
/// Live session cannot be driven. It reads the source, writes one length-preserving WAV
/// per requested stem into the same folder Live uses, and can inject the failure modes
/// the batch is expected to survive.
///
/// Results produced here are explicitly *simulated* and are never presented as Ableton
/// verification.
/// </summary>
public sealed class SimulatedLiveRunner : ISeparator
{
    public enum FaultMode
    {
        None,
        LiveCrashed,
        LostFocus,
        UnexpectedDialog,
        NoStems,
        SeparationCancelled,
        SeparationTimeout,
        LiveStartupFailed,
    }

    public sealed class Options
    {
        public FaultMode Fault { get; init; } = FaultMode.None;

        /// <summary>Multiplier on the simulated processing time (1.0 = realistic).</summary>
        public double SpeedFactor { get; init; } = 1.0;

        /// <summary>Write AIFF instead of WAV, to exercise the conversion path.</summary>
        public bool EmitAiff { get; init; }
    }

    private readonly RunLog _log;
    private readonly Func<SeparationRequest, Options> _optionsFactory;

    public string Name => "Simulated engine";

    /// <summary>
    /// Live, on purpose: what this double reproduces is Live's contract, including the failures
    /// only Live can raise. Classifying it as anything else would let those failures be rewritten
    /// on the way into the queue and defeat the tests that exist to check them.
    /// </summary>
    public EngineKind Kind => EngineKind.Live;

    public SimulatedLiveRunner(RunLog log, Func<SeparationRequest, Options>? optionsFactory = null)
    {
        _log = log;
        _optionsFactory = optionsFactory ?? (_ => new Options());
    }

    public async Task<SeparationOutcome> SeparateAsync(SeparationRequest request, CancellationToken cancellationToken)
    {
        var options = _optionsFactory(request);
        var stemFolder = request.LiveStemsFolder;
        Directory.CreateDirectory(stemFolder);

        request.Progress?.Report(new SeparationProgress(JobStage.LaunchingLive, "simulated"));
        await Task.Delay(TimeSpan.FromMilliseconds(120 * options.SpeedFactor), cancellationToken).ConfigureAwait(false);

        switch (options.Fault)
        {
            case FaultMode.LiveStartupFailed:
                return SeparationOutcome.Failure(ErrorKind.LiveStartupFailed, "Simulated: Live did not start.");
            case FaultMode.LiveCrashed:
                return SeparationOutcome.Failure(ErrorKind.LiveCrashed, "Simulated: Live exited unexpectedly.");
            case FaultMode.LostFocus:
                return SeparationOutcome.Failure(ErrorKind.LiveLostFocus, "Simulated: Live lost the foreground.");
            case FaultMode.UnexpectedDialog:
                return SeparationOutcome.Failure(ErrorKind.UnexpectedDialog, "Simulated: an unexpected dialog appeared.");
            case FaultMode.NoStems:
                await Task.Delay(TimeSpan.FromMilliseconds(200 * options.SpeedFactor), cancellationToken).ConfigureAwait(false);
                return SeparationOutcome.Failure(ErrorKind.StemsMissing, "Simulated: Live produced no stems.");
            case FaultMode.SeparationCancelled:
                await Task.Delay(TimeSpan.FromMilliseconds(200 * options.SpeedFactor), cancellationToken).ConfigureAwait(false);
                return SeparationOutcome.Cancelled(ErrorKind.SeparationCancelled, "Simulated: separation was cancelled in Live.");
            case FaultMode.SeparationTimeout:
                await Task.Delay(TimeSpan.FromMilliseconds(200 * options.SpeedFactor), cancellationToken).ConfigureAwait(false);
                return SeparationOutcome.Failure(ErrorKind.SeparationTimeout, "Simulated: separation timed out.");
        }

        request.Progress?.Report(new SeparationProgress(JobStage.Separating, "simulated"));

        var sourceInfo = request.SourceInfo;

        // An MP3 is measured, never decoded, so a simulated MP3 run shapes its stems from
        // silence of the right length instead of samples this tool cannot obtain.
        var sources = AudioFileReader.HasDecodableSamples(request.Source.Path)
            ? AudioFileReader.ReadSamples(request.Source.Path, out _)
            : new float[sourceInfo.SampleFrames * sourceInfo.Channels];

        // Simulated processing time, deliberately separate from any copying.
        var simulatedSeconds = Math.Min(3.0, sourceInfo.DurationSeconds * 0.02) * options.SpeedFactor;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(simulatedSeconds), cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var generated = new Dictionary<Stems, string>();
        foreach (var stem in request.Profile.Stems.Enumerate())
        {
            var samples = RenderStem(sources, sourceInfo.Channels, stem);
            var fileName = options.EmitAiff
                ? Path.ChangeExtension(stem.OutputFileName(), ".aif")
                : stem.OutputFileName();
            var path = Path.Combine(stemFolder, fileName);

            if (options.EmitAiff)
                AiffWriter.WriteFloat32(path, samples, sourceInfo.Channels, sourceInfo.SampleRate);
            else
                WavWriter.WriteFloat32(path, samples, sourceInfo.Channels, sourceInfo.SampleRate);

            generated[stem] = path;
            _log.Info($"simulated {stem.DisplayName()} -> {fileName}", request.Job);
        }

        request.Progress?.Report(new SeparationProgress(JobStage.CollectingStems, "simulated"));
        return new SeparationOutcome(true, ErrorKind.None, null, generated, stopwatch.Elapsed.TotalSeconds, false);
    }

    /// <summary>
    /// Crude, reconstruction-friendly split of the source so each stem is a distinct but
    /// plausibly related signal. The point is verifiable length and identity, not audio quality.
    /// </summary>
    private static float[] RenderStem(float[] source, int channels, Stems stem)
    {
        var result = new float[source.Length];

        // First-order filters, per channel, keeping the splits additive.
        var bassCutoff = 200.0;
        var vocalLowCut = 300.0;
        var vocalHighCut = 4000.0;
        var drumHighCut = 5000.0;

        for (var ch = 0; ch < channels; ch++)
        {
            double bassState = 0;
            double vocalLowState = 0;
            double vocalHighState = 0;
            double drumHighState = 0;

            var alphaBass = Alpha(bassCutoff);
            var alphaVocalLow = Alpha(vocalLowCut);
            var alphaVocalHigh = Alpha(vocalHighCut);
            var alphaDrumHigh = Alpha(drumHighCut);

            for (var i = ch; i < source.Length; i += channels)
            {
                double x = source[i];

                bassState += alphaBass * (x - bassState);
                drumHighState += alphaDrumHigh * (x - drumHighState);
                vocalLowState += alphaVocalLow * (x - vocalLowState);
                vocalHighState += alphaVocalHigh * (x - vocalHighState);

                var bass = bassState;
                var high = x - drumHighState;
                var vocalBand = vocalLowState - vocalHighState;

                result[i] = stem switch
                {
                    Stems.Bass => (float)(bass * 0.9),
                    Stems.Vocals => (float)(vocalBand * 2.0),
                    Stems.Drums => (float)(high * 0.8),
                    Stems.Others => (float)((x - bass - vocalBand - high) * 0.9),
                    _ => 0f,
                };
            }
        }

        return result;
    }

    private static double Alpha(double cutoffHz, double sampleRate = 44100) =>
        1.0 - Math.Exp(-2.0 * Math.PI * cutoffHz / sampleRate);
}

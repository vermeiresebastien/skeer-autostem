using StemForge.Core.Models;

namespace StemForge.Core.Local;

/// <summary>
/// Collects the model's output for each window into finished stems.
///
/// The accumulation is the other half of demucs' <c>apply_model</c> contract: a weighted sum of
/// the overlapping windows divided by the weights that actually landed there, which is why a
/// region only touched by the edge of one window is not quietly attenuated. Output is kept
/// planar - stem, then channel, then sample - because that is the order the model answers in
/// and because a stem is written to its own file.
/// </summary>
public sealed class DemucsOverlapAccumulator
{
    private readonly DemucsPlan _plan;
    private readonly int _channels;
    private readonly float[] _fade;
    private readonly float[] _stems;
    private readonly double[] _weights;

    public DemucsOverlapAccumulator(DemucsPlan plan, int totalSamples, IReadOnlyList<Stems> stems, int channels)
    {
        if (totalSamples <= 0) throw new ArgumentOutOfRangeException(nameof(totalSamples));
        if (stems.Count == 0) throw new ArgumentException("Nothing to accumulate.", nameof(stems));
        if (channels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(channels));

        _plan = plan;
        _channels = channels;
        _fade = plan.FadeWeight();
        _stems = new float[stems.Count * channels * totalSamples];
        _weights = new double[totalSamples];
        SampleCount = stems.Count;
    }

    public int SampleCount { get; }

    /// <summary>Total fade weight that reached one sample. Zero means the sample was never covered.</summary>
    public double WeightAt(int sample) => _weights[sample];

    /// <summary>
    /// Adds one model answer. <paramref name="segmentOutput"/> is the raw output for the window:
    /// stem-major, then channel-major, then <see cref="DemucsPlan.SegmentLength"/> samples, with
    /// the silence the model was shown still in place.
    /// </summary>
    public void Add(DemucsWindow window, ReadOnlySpan<float> segmentOutput)
    {
        if (segmentOutput.Length != _plan.SegmentLength * SampleCount * _channels)
            throw new ArgumentException(
                $"Expected a whole window ({_plan.SegmentLength * SampleCount * _channels} samples), got {segmentOutput.Length}.",
                nameof(segmentOutput));

        // The window starts before the chunk, so the model's first PadLeft answers are for audio
        // earlier in the track, and its last ones may be for silence past the end. Both are
        // dropped, exactly as the reference trims the window back to the chunk it came from.
        var source = window.PadLeft;

        for (var stem = 0; stem < SampleCount; stem++)
        {
            for (var channel = 0; channel < _channels; channel++)
            {
                var outputBase = (stem * _channels + channel) * _plan.SegmentLength + source;
                var targetBase = (stem * _channels + channel) * _weights.Length + window.Offset;

                for (var i = 0; i < window.ChunkLength; i++)
                {
                    _stems[targetBase + i] += _fade[i] * segmentOutput[outputBase + i];
                }
            }
        }

        for (var i = 0; i < window.ChunkLength; i++)
        {
            _weights[window.Offset + i] += _fade[i];
        }
    }

    /// <summary>
    /// Divides by the accumulated weight and returns one interleaved stem, ready for
    /// <c>WavWriter</c>. A sample no window reached stays silent rather than being divided by
    /// zero - and <see cref="WeightAt"/> is how a caller finds out whether that happened.
    /// </summary>
    public float[] Complete(int stemIndex)
    {
        if ((uint)stemIndex >= SampleCount) throw new ArgumentOutOfRangeException(nameof(stemIndex));

        var total = _weights.Length;
        var interleaved = new float[total * _channels];

        for (var channel = 0; channel < _channels; channel++)
        {
            var planar = (stemIndex * _channels + channel) * total;
            for (var i = 0; i < total; i++)
            {
                var weight = _weights[i];
                interleaved[i * _channels + channel] = weight > 0 ? (float)(_stems[planar + i] / weight) : 0f;
            }
        }

        return interleaved;
    }
}

namespace StemForge.Core.Local;

/// <summary>
/// The one level decision demucs makes before a model sees a track, and its inverse.
///
/// demucs' <c>Separator.separate_wave</c> takes the mono reference of the mix, measures its mean
/// and standard deviation, hands the model <c>(wav - mean) / std</c> and puts the model back into
/// the room with <c>out * std + mean</c>. That is the whole reason a Demucs stem set is as loud as
/// the track it came from, so it is ported rather than reinvented: an engine that quietly gains
/// the stems would make every later comparison - against Live, against the source, between two
/// runs - mean nothing.
/// </summary>
public readonly record struct DemucsLevel(float Mean, float Std)
{
    /// <summary>
    /// Measures a stereo mix the way the reference engine does: the reference is the mean of the
    /// two channels, and its standard deviation is the unbiased one (Python's default, N-1).
    /// Accumulating in double only makes the divisor steadier than float32 could; the difference
    /// is far below anything a stem can show.
    /// </summary>
    public static DemucsLevel FromStereo(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length) throw new ArgumentException("Channels are not the same length.");
        if (left.Length < 2) throw new ArgumentException("A one-sample track has no standard deviation.", nameof(left));

        double sum = 0;
        double sumSquares = 0;

        for (var i = 0; i < left.Length; i++)
        {
            var mono = 0.5f * (left[i] + right[i]);
            sum += mono;
            sumSquares += (double)mono * mono;
        }

        var mean = (float)(sum / left.Length);
        var variance = (sumSquares - sum * sum / left.Length) / (left.Length - 1);
        var std = (float)Math.Sqrt(Math.Max(0, variance)) + 1e-8f;

        return new DemucsLevel(mean, std);
    }

    /// <summary>Scales one channel in place on the way into the model.</summary>
    public void Apply(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (samples[i] - Mean) / Std;
        }
    }

    /// <summary>Scales one channel back on the way out, restoring the track's own level.</summary>
    public void Undo(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = samples[i] * Std + Mean;
        }
    }
}

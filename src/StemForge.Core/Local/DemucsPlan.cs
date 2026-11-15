namespace StemForge.Core.Local;

/// <summary>
/// Where a track is cut for the model, and how the pieces are put back.
///
/// This is a literal port of the split/overlap-add arithmetic in demucs 4.1.0
/// (<c>demucs/apply.py</c>, <c>apply_model</c>, the <c>split</c> branch), kept deliberately
/// identical to the reference engine: the Phase 2 gate is that our engine can be compared to
/// Python's in decibels, and that comparison is only meaningful while the segmentation, the
/// window padding and the fade weights match sample for sample.
/// </summary>
public sealed record DemucsPlan(int SampleRate, double SegmentSeconds, double Overlap, double TransitionPower = 1.0)
{
    /// <summary>htdemucs as the demucs CLI runs it: a 10 s window, 25 % overlap.</summary>
    public static DemucsPlan Htdemucs { get; } = new(44100, 10.0, 0.25);

    public int SegmentLength { get; } = checked((int)(SampleRate * SegmentSeconds));

    public int Stride => checked((int)((1.0 - Overlap) * SegmentLength));

    /// <summary>
    /// The windows covering a track of <paramref name="totalSamples"/>, in order. A window can
    /// reach past the end of the track; what happens there is the whole point of
    /// <see cref="DemucsWindow"/>, so it is described rather than hidden.
    /// </summary>
    public IReadOnlyList<DemucsWindow> Windows(int totalSamples)
    {
        if (totalSamples <= 0) throw new ArgumentOutOfRangeException(nameof(totalSamples));
        if (Stride <= 0) throw new InvalidOperationException("Overlap must leave a positive stride.");

        var windows = new List<DemucsWindow>();
        for (var offset = 0; offset < totalSamples; offset += Stride)
        {
            var chunkLength = Math.Min(SegmentLength, totalSamples - offset);

            // demucs centres the chunk inside a full window rather than padding after it, so the
            // model always sees context on both sides of what it is being asked about. Python's
            // `delta // 2` is a floor division, and delta is never negative here, so C# integer
            // division gives the same offset.
            var delta = SegmentLength - chunkLength;
            var windowStart = offset - delta / 2;

            windows.Add(new DemucsWindow(offset, chunkLength, windowStart, SegmentLength));
        }

        return windows;
    }

    /// <summary>
    /// The fade applied to every window: a triangle rising to 1 in the middle and falling back,
    /// which is what makes the overlapping windows sum smoothly instead of stepping.
    /// </summary>
    public float[] FadeWeight()
    {
        var half = SegmentLength / 2;
        if (half <= 0) throw new InvalidOperationException("The segment is too short to fade.");

        var weight = new float[SegmentLength];
        for (var i = 0; i < SegmentLength; i++)
        {
            var ramp = i < half ? i + 1 : SegmentLength - i;
            weight[i] = (float)Math.Pow(ramp / (double)half, TransitionPower);
        }

        return weight;
    }
}

/// <summary>
/// One model call: where it starts in the track, how much real audio it holds, and where its
/// window begins in the source.
/// </summary>
/// <param name="Offset">Track sample the chunk begins at, and where its first output sample lands.</param>
/// <param name="ChunkLength">Real samples in the chunk - the segment length, except at the end.</param>
/// <param name="windowStart">Where the model's window begins. The reference centres the chunk in a
/// full window, so this is earlier than <paramref name="offset"/> and anything past the track end
/// is silence.</param>
public readonly record struct DemucsWindow(int Offset, int ChunkLength, int WindowStart, int WindowLength)
{
    /// <summary>
    /// How far inside the window the chunk starts. The model answers for the whole window, and
    /// these samples are dropped before accumulating - the mirror of <c>center_trim(out, length)</c>
    /// in demucs' <c>apply_model</c>. They are audio earlier in the track, already covered by
    /// earlier windows, unless the window begins before the track itself.
    /// </summary>
    public int PadLeft => Offset - WindowStart;

    /// <summary>First sample of the window that is inside the track.</summary>
    public int ReadStart => Math.Max(0, WindowStart);

    /// <summary>One past the last sample of the window that is inside the track.</summary>
    public int ReadEnd(int totalSamples) => Math.Min(totalSamples, WindowStart + WindowLength);
}

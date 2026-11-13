namespace StemForge.Core.Audio;

public enum WavSampleFormat
{
    Pcm,
    Float,
    Unknown,
}

/// <summary>Parsed header information for a RIFF/WAVE or AIFF file.</summary>
public sealed record WavInfo(
    WavSampleFormat Format,
    int Channels,
    int SampleRate,
    int BitsPerSample,
    long DataByteCount,
    long SampleFrames)
{
    /// <summary>Set for AIFF 'sowt' files, which store samples little-endian.</summary>
    public bool LittleEndian { get; init; }

    public double DurationSeconds => SampleRate > 0 ? (double)SampleFrames / SampleRate : 0;

    public int BlockAlign => Channels * Math.Max(1, BitsPerSample / 8);

    public override string ToString() =>
        $"{Format} {BitsPerSample}-bit {Channels}ch {SampleRate}Hz {DurationSeconds:F3}s";
}

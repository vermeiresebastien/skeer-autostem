using System.Text;

namespace StemForge.Core.Audio;

/// <summary>
/// Writes 32-bit IEEE float WAV files. Used by the simulated runner and by the
/// format-conversion step when Live produces a supported non-WAV file.
/// Writes sample data verbatim: no normalisation, effects or time stretching.
/// </summary>
public static class WavWriter
{
    public static void WriteFloat32(string path, float[] interleaved, int channels, int sampleRate)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var dataBytes = interleaved.Length * 4;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs, Encoding.ASCII);

        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));

        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((ushort)3);                       // IEEE float
        w.Write((ushort)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * 4);       // byte rate
        w.Write((ushort)(channels * 4));          // block align
        w.Write((ushort)32);                      // bits per sample

        w.Write(Encoding.ASCII.GetBytes("fact"));
        w.Write(4);
        w.Write(interleaved.Length / channels);

        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);

        var buffer = new byte[64 * 1024];
        var position = 0;
        while (position < interleaved.Length)
        {
            var count = Math.Min(buffer.Length / 4, interleaved.Length - position);
            Buffer.BlockCopy(interleaved, position * 4, buffer, 0, count * 4);
            w.Write(buffer, 0, count * 4);
            position += count;
        }
    }
}

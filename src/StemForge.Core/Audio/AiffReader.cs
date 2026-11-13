using System.Text;

namespace StemForge.Core.Audio;

/// <summary>
/// Reader for uncompressed AIFF / AIFC files. Ableton Live can be configured to record
/// in AIFF, so this exists purely to convert such output to the requested WAV without
/// touching the audio (no normalisation, effects or time stretching).
/// Reads only through the FileStream to avoid buffered-reader/seek desynchronisation.
/// </summary>
public static class AiffReader
{
    public static bool IsAiff(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return HasFormHeader(fs);
        }
        catch
        {
            return false;
        }
    }

    public static WavInfo ReadInfo(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var (info, _) = Scan(fs, Path.GetFileName(path));
        return info;
    }

    public static float[] ReadSamples(string path, out WavInfo info)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var (parsed, soundOffset) = Scan(fs, Path.GetFileName(path));
        info = parsed;

        if (soundOffset < 0)
            throw new InvalidDataException($"'{Path.GetFileName(path)}' has no SSND chunk.");

        var bytesPerSample = Math.Max(1, parsed.BitsPerSample / 8);
        var available = (int)Math.Min(
            (fs.Length - soundOffset) / Math.Max(1, bytesPerSample),
            (long)int.MaxValue);

        fs.Position = soundOffset;
        var raw = new byte[available * bytesPerSample];
        fs.ReadExactly(raw, 0, raw.Length);

        var sampleCount = Math.Min(parsed.SampleFrames * parsed.Channels, raw.Length / bytesPerSample);
        var samples = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var offset = i * bytesPerSample;
            samples[i] = parsed.BitsPerSample switch
            {
                8 => (raw[offset] - 128) / 128f,
                16 => (parsed.LittleEndian
                        ? BitConverter.ToInt16(raw, offset)
                        : (short)((raw[offset] << 8) | raw[offset + 1])) / 32768f,
                24 => Decode24(raw, offset, parsed.LittleEndian) / 8388608f,
                32 => Decode32(raw, offset, parsed.LittleEndian) / 2147483648f,
                _ => 0f,
            };
        }

        return samples;
    }

    private static (WavInfo Info, long SoundOffset) Scan(FileStream fs, string displayName)
    {
        if (!HasFormHeader(fs))
            throw new InvalidDataException($"'{displayName}' is not an AIFF file.");

        fs.Position = 0;
        var header = new byte[12];
        fs.ReadExactly(header, 0, 12);
        var kind = Encoding.ASCII.GetString(header, 8, 4);

        int channels = 0, bits = 0, sampleRate = 0;
        long frames = 0;
        var littleEndian = false;
        long soundOffset = -1;

        while (fs.Position + 8 <= fs.Length)
        {
            var chunkHeader = new byte[8];
            fs.ReadExactly(chunkHeader, 0, 8);
            var id = Encoding.ASCII.GetString(chunkHeader, 0, 4);
            var size = (long)BitConverter.ToUInt32(new[] { chunkHeader[7], chunkHeader[6], chunkHeader[5], chunkHeader[4] }, 0);
            var chunkStart = fs.Position;
            var chunkEnd = Math.Min(chunkStart + size, fs.Length);

            if (id == "COMM" && size >= 18)
            {
                var comm = new byte[Math.Min(size, 64)];
                fs.ReadExactly(comm, 0, comm.Length);

                channels = (comm[0] << 8) | comm[1];
                frames = ((long)comm[2] << 24) | ((long)comm[3] << 16) | ((long)comm[4] << 8) | comm[5];
                bits = (comm[6] << 8) | comm[7];
                sampleRate = (int)Math.Round(ReadExtended80(comm.AsSpan(8, 10)));

                if (kind == "AIFC" && comm.Length >= 22)
                {
                    var compression = Encoding.ASCII.GetString(comm, 18, 4);
                    littleEndian = compression == "sowt";
                }
            }
            else if (id == "SSND")
            {
                // 4 byte offset + 4 byte block size, then the samples.
                var ssndHeader = new byte[8];
                fs.ReadExactly(ssndHeader, 0, 8);
                var offset = BitConverter.ToUInt32(new[] { ssndHeader[3], ssndHeader[2], ssndHeader[1], ssndHeader[0] }, 0);
                soundOffset = chunkStart + 8 + offset;
            }

            fs.Position = Math.Min(chunkEnd + (chunkEnd % 2), fs.Length);
        }

        if (channels <= 0 || sampleRate <= 0)
        {
            throw new InvalidDataException(
                $"'{displayName}' has no usable COMM chunk " +
                $"(channels={channels}, sampleRate={sampleRate}, bits={bits}, frames={frames}, kind={kind}).");
        }

        var blockAlign = channels * Math.Max(1, bits / 8);
        return (new WavInfo(WavSampleFormat.Pcm, channels, sampleRate, bits, frames * blockAlign, frames)
        {
            LittleEndian = littleEndian,
        }, soundOffset);
    }

    private static bool HasFormHeader(FileStream fs)
    {
        if (fs.Length < 12) return false;
        var header = new byte[12];
        fs.ReadExactly(header, 0, 12);
        fs.Position = 0;
        var form = Encoding.ASCII.GetString(header, 0, 4);
        var kind = Encoding.ASCII.GetString(header, 8, 4);
        return form == "FORM" && (kind == "AIFF" || kind == "AIFC");
    }

    private static int Decode24(byte[] b, int o, bool littleEndian)
    {
        int value = littleEndian
            ? b[o] | (b[o + 1] << 8) | (b[o + 2] << 16)
            : (b[o] << 16) | (b[o + 1] << 8) | b[o + 2];
        if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
        return value;
    }

    private static int Decode32(byte[] b, int o, bool littleEndian) => littleEndian
        ? BitConverter.ToInt32(b, o)
        : (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

    /// <summary>
    /// Decodes the 80-bit IEEE extended float AIFF uses for sample rates.
    /// The 64-bit mantissa of a normalised value has its top bit set, so it must be
    /// held as an unsigned value - as a signed long it would go negative.
    /// </summary>
    private static double ReadExtended80(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10) return 0;
        var exponent = ((bytes[0] & 0x7F) << 8) | bytes[1];
        ulong mantissa = 0;
        for (var i = 2; i < 10; i++) mantissa = (mantissa << 8) | bytes[i];
        if (exponent == 0 && mantissa == 0) return 0;
        var sign = (bytes[0] & 0x80) != 0 ? -1 : 1;
        return sign * (double)mantissa * Math.Pow(2, exponent - 16383 - 63);
    }
}

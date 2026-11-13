using System.Text;

namespace StemForge.Core.Audio;

/// <summary>
/// Minimal, dependency-free RIFF/WAVE reader. Handles PCM (8/16/24/32 bit),
/// IEEE float (32/64 bit) and WAVE_FORMAT_EXTENSIBLE with PCM or float sub-formats,
/// which covers everything Ableton Live writes for stem exports.
///
/// Reads exclusively through the FileStream: mixing a buffered BinaryReader with
/// manual seeks silently desynchronises the reader, so that is deliberately avoided.
/// </summary>
public static class WavReader
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    private static readonly Guid SubFormatPcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubFormatFloat = new("00000003-0000-0010-8000-00aa00389b71");

    public static bool IsWav(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return HasRiffWaveHeader(fs);
        }
        catch
        {
            return false;
        }
    }

    public static WavInfo ReadInfo(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ReadInfo(fs, Path.GetFileName(path));
    }

    private static WavInfo ReadInfo(FileStream fs, string displayName)
    {
        if (!HasRiffWaveHeader(fs))
            throw new InvalidDataException($"'{displayName}' is not a RIFF/WAVE file.");

        fs.Position = 12;

        WavSampleFormat format = WavSampleFormat.Unknown;
        int channels = 0, sampleRate = 0, bits = 0;
        long dataBytes = -1;
        long factFrames = -1;

        while (fs.Position + 8 <= fs.Length)
        {
            var header = new byte[8];
            fs.ReadExactly(header, 0, 8);
            var id = Encoding.ASCII.GetString(header, 0, 4);
            var size = BitConverter.ToUInt32(header, 4);
            var chunkStart = fs.Position;
            var chunkEnd = Math.Min(chunkStart + size, fs.Length);

            switch (id)
            {
                case "fmt ":
                    var fmt = new byte[Math.Min(size, 40)];
                    fs.ReadExactly(fmt, 0, fmt.Length);

                    var audioFormat = BitConverter.ToUInt16(fmt, 0);
                    channels = BitConverter.ToUInt16(fmt, 2);
                    sampleRate = BitConverter.ToInt32(fmt, 4);
                    bits = BitConverter.ToUInt16(fmt, 14);

                    if (audioFormat == FormatExtensible && fmt.Length >= 26)
                    {
                        var subFormat = new Guid(fmt.AsSpan(24, 16));
                        audioFormat = subFormat == SubFormatFloat ? FormatFloat
                                    : subFormat == SubFormatPcm ? FormatPcm
                                    : (ushort)0;
                    }

                    format = audioFormat switch
                    {
                        FormatPcm => WavSampleFormat.Pcm,
                        FormatFloat => WavSampleFormat.Float,
                        _ => WavSampleFormat.Unknown,
                    };
                    break;

                case "data":
                    dataBytes = size;
                    break;

                case "fact":
                    if (size >= 4)
                    {
                        var fact = new byte[4];
                        fs.ReadExactly(fact, 0, 4);
                        factFrames = BitConverter.ToUInt32(fact, 0);
                    }
                    break;
            }

            // Chunks are word aligned.
            fs.Position = Math.Min(chunkEnd + (chunkEnd % 2), fs.Length);
        }

        if (dataBytes < 0)
            throw new InvalidDataException($"'{displayName}' has no 'data' chunk.");

        if (format == WavSampleFormat.Unknown || channels <= 0 || sampleRate <= 0 || bits <= 0)
            throw new InvalidDataException($"'{displayName}' uses an unsupported WAV encoding.");

        var blockAlign = Math.Max(1, channels * Math.Max(1, bits / 8));
        var frames = factFrames >= 0 ? factFrames : dataBytes / blockAlign;

        return new WavInfo(format, channels, sampleRate, bits, dataBytes, frames);
    }

    private static bool HasRiffWaveHeader(FileStream fs)
    {
        if (fs.Length < 12) return false;
        var header = new byte[12];
        fs.ReadExactly(header, 0, 12);
        fs.Position = 0;
        return Encoding.ASCII.GetString(header, 0, 4) == "RIFF"
            && Encoding.ASCII.GetString(header, 8, 4) == "WAVE";
    }

    /// <summary>Reads all sample frames as normalised interleaved floats.</summary>
    public static float[] ReadSamples(string path, out WavInfo info)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        info = ReadInfo(fs, Path.GetFileName(path));

        var samples = new float[info.SampleFrames * info.Channels];
        if (samples.Length == 0) return samples;

        fs.Position = FindDataChunkOffset(fs);

        var bytesPerSample = Math.Max(1, info.BitsPerSample / 8);
        var frameBytes = bytesPerSample * info.Channels;
        var buffer = new byte[frameBytes];
        var index = 0;

        for (long frame = 0; frame < info.SampleFrames && fs.Position + frameBytes <= fs.Length; frame++)
        {
            fs.ReadExactly(buffer, 0, frameBytes);
            for (var ch = 0; ch < info.Channels; ch++)
                samples[index++] = DecodeSample(buffer, ch * bytesPerSample, info);
        }

        return samples;
    }

    private static long FindDataChunkOffset(FileStream fs)
    {
        fs.Position = 12;
        while (fs.Position + 8 <= fs.Length)
        {
            var header = new byte[8];
            fs.ReadExactly(header, 0, 8);
            var id = Encoding.ASCII.GetString(header, 0, 4);
            var size = BitConverter.ToUInt32(header, 4);
            if (id == "data") return fs.Position;
            fs.Position = Math.Min(fs.Position + size + (size % 2), fs.Length);
        }
        throw new InvalidDataException("No 'data' chunk.");
    }

    private static float DecodeSample(byte[] buffer, int offset, WavInfo info)
    {
        if (info.Format == WavSampleFormat.Float)
        {
            return info.BitsPerSample switch
            {
                32 => BitConverter.ToSingle(buffer, offset),
                64 => (float)BitConverter.ToDouble(buffer, offset),
                _ => 0f,
            };
        }

        return info.BitsPerSample switch
        {
            8 => (buffer[offset] - 128) / 128f,
            16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            24 => Decode24(buffer, offset) / 8388608f,
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0f,
        };
    }

    private static int Decode24(byte[] b, int o)
    {
        var value = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
        if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
        return value;
    }
}

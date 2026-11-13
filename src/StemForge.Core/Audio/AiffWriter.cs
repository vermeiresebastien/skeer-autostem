using System.Text;

namespace StemForge.Core.Audio;

/// <summary>
/// Writes 24-bit big-endian AIFF. Used by the simulated runner to exercise the
/// non-WAV conversion path. Sample values are written verbatim.
///
/// AIFF is a big-endian container, so multi-byte fields are encoded explicitly rather
/// than through BinaryWriter (which is little-endian).
/// </summary>
public static class AiffWriter
{
    public static void WriteFloat32(string path, float[] interleaved, int channels, int sampleRate)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var frames = interleaved.Length / channels;
        var dataBytes = frames * channels * 3;

        // COMM payload: channels(2) frames(4) bits(2) rate(10) = 18 bytes.
        const int commSize = 18;
        var formSize = 4                        // 'AIFF'
                     + 8 + commSize             // COMM chunk
                     + 8 + 8 + dataBytes;       // SSND chunk (8 byte header + data)

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);

        WriteAscii(fs, "FORM");
        WriteInt32Be(fs, formSize);
        WriteAscii(fs, "AIFF");

        WriteAscii(fs, "COMM");
        WriteInt32Be(fs, commSize);
        WriteInt16Be(fs, (short)channels);
        WriteInt32Be(fs, (int)frames);
        WriteInt16Be(fs, 24);
        fs.Write(EncodeExtended80(sampleRate));

        WriteAscii(fs, "SSND");
        WriteInt32Be(fs, 8 + dataBytes);
        WriteInt32Be(fs, 0);   // offset
        WriteInt32Be(fs, 0);   // block size

        var buffer = new byte[64 * 1024];
        var position = 0;
        while (position < interleaved.Length)
        {
            var count = Math.Min(buffer.Length / 3, interleaved.Length - position);
            for (var i = 0; i < count; i++)
            {
                var clamped = Math.Clamp(interleaved[position + i], -1f, 1f);
                var value = (int)Math.Round(clamped * 8388607f);
                buffer[i * 3] = (byte)((value >> 16) & 0xFF);
                buffer[i * 3 + 1] = (byte)((value >> 8) & 0xFF);
                buffer[i * 3 + 2] = (byte)(value & 0xFF);
            }

            fs.Write(buffer, 0, count * 3);
            position += count;
        }
    }

    private static void WriteAscii(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));

    private static void WriteInt32Be(Stream stream, int value) =>
        stream.Write(new[]
        {
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF),
        });

    private static void WriteInt16Be(Stream stream, short value) =>
        stream.Write(new[] { (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF) });

    /// <summary>
    /// Encodes an integer as the 80-bit IEEE extended float AIFF uses. The mantissa is
    /// held unsigned: a normalised value has its top bit set, which would be the sign
    /// bit if it were stored in a signed long.
    /// </summary>
    private static byte[] EncodeExtended80(int value)
    {
        var bytes = new byte[10];
        if (value == 0) return bytes;

        var exponent = 16383 + 63;
        ulong mantissa = (ulong)value;
        const ulong HighBit = 0x8000000000000000UL;

        while ((mantissa & HighBit) == 0 && mantissa != 0)
        {
            mantissa <<= 1;
            exponent--;
        }

        bytes[0] = (byte)((exponent >> 8) & 0x7F);
        bytes[1] = (byte)(exponent & 0xFF);
        for (var i = 0; i < 8; i++)
        {
            bytes[9 - i] = (byte)(mantissa & 0xFF);
            mantissa >>= 8;
        }

        return bytes;
    }
}

namespace StemForge.Core.Audio;

/// <summary>
/// Dependency-free MPEG Layer III container reader. It counts frames rather than decoding
/// them: Live opens the source in place, so the only things this tool needs from an MP3 are
/// the sample rate and the frame count that put the clip markers on the whole file.
///
/// The frame walk is used instead of the Xing/VBRI header because it is exact for variable
/// bitrate files as well, and it needs no vendor-specific extension tables.
/// </summary>
public static class Mp3Reader
{
    private const uint SyncMask = 0xFFE0_0000;
    private const int JunkScanLimit = 1 << 20;

    private static readonly int[] BitratesV1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
    private static readonly int[] BitratesV2L3 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
    private static readonly int[] RatesV1 = { 44100, 48000, 32000 };
    private static readonly int[] RatesV2 = { 22050, 24000, 16000 };
    private static readonly int[] RatesV25 = { 11025, 12000, 8000 };

    private struct Frame
    {
        public int SampleRate;
        public int Channels;
        public int SamplesPerFrame;
        public int LengthBytes;
    }

    public static bool IsMp3(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return FindAudioStart(fs, out _) >= 0;
        }
        catch
        {
            return false;
        }
    }

    public static WavInfo ReadInfo(string path)
    {
        var name = Path.GetFileName(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var start = FindAudioStart(fs, out var first);
        if (start < 0) throw new InvalidDataException($"'{name}' is not an MPEG Layer III file.");

        using var buffered = new BufferedStream(fs, 1 << 16);
        buffered.Position = start;

        var frames = 0L;
        var position = start;

        while (true)
        {
            if (!TryReadFrame(buffered, out var frame))
            {
                var after = SkipId3TagAt(buffered, position);
                if (after < 0) break;

                buffered.Position = after;
                position = after;
                continue;
            }

            frames++;
            position += frame.LengthBytes;
            if (position >= fs.Length) break;
        }

        return new WavInfo(
            WavSampleFormat.Unknown,
            first.Channels,
            first.SampleRate,
            BitsPerSample: 0,
            DataByteCount: position - start,
            SampleFrames: frames * first.SamplesPerFrame);
    }

    /// <summary>
    /// Position of the first frame header, past any leading ID3v2 tag, or -1 when the file
    /// does not look like MPEG audio. A mislabelled container has to be rejected here, because
    /// everything downstream trusts the length this reader reports.
    /// </summary>
    private static long FindAudioStart(Stream stream, out Frame first)
    {
        first = default;

        var scanned = 0;
        long position = 0;

        while (position + 4 <= stream.Length && scanned < JunkScanLimit)
        {
            if (IsId3TagAt(stream, position))
            {
                var after = SkipId3TagAt(stream, position);
                if (after < 0) return -1;

                position = after;
                scanned += 10;
                continue;
            }

            // A second header exactly one frame length later is the test: a single one can
            // appear by chance in any binary file, a pair only occurs in real MPEG audio.
            if (TryParseHeader(PeekAt(stream, position), out first) &&
                TryParseHeader(PeekAt(stream, position + first.LengthBytes), out _))
                return position;

            position++;
            scanned++;
        }

        return -1;
    }

    private static bool TryParseHeader(uint? header, out Frame frame)
    {
        frame = default;

        if (header is not { } value) return false;
        if ((value & SyncMask) != SyncMask) return false;

        var version = (byte)((value >> 19) & 3);
        if (version == 1) return false;
        if ((byte)((value >> 17) & 3) != 1) return false;

        var bitrateIndex = (byte)((value >> 12) & 0xF);
        var rateIndex = (byte)((value >> 10) & 3);
        if (rateIndex == 3) return false;

        var bitrate = (version == 3 ? BitratesV1L3 : BitratesV2L3)[bitrateIndex];
        if (bitrate == 0) return false;

        var sampleRate = version switch
        {
            3 => RatesV1[rateIndex],
            2 => RatesV2[rateIndex],
            _ => RatesV25[rateIndex],
        };

        var length = (long)(version == 3 ? 144 : 72) * bitrate * 1000 / sampleRate + (value >> 9 & 1);
        if (length < 16) return false;

        frame = new Frame
        {
            SampleRate = sampleRate,
            Channels = ((value >> 6) & 3) == 3 ? 1 : 2,
            SamplesPerFrame = version == 3 ? 1152 : 576,
            LengthBytes = (int)length,
        };

        return true;
    }

    /// <summary>Consumes a frame header and the frame body it describes.</summary>
    private static bool TryReadFrame(Stream stream, out Frame frame)
    {
        frame = default;

        var origin = stream.Position;
        if (!TryParseHeader(PeekAt(stream, origin), out frame)) return false;

        stream.Position = origin;
        var buffer = new byte[4];
        if (ReadUpTo(stream, buffer, 4) < 4) return false;

        stream.Seek(frame.LengthBytes - 4, SeekOrigin.Current);
        return true;
    }

    private static uint? PeekAt(Stream stream, long position)
    {
        if (position < 0 || position + 4 > stream.Length) return null;

        var origin = stream.Position;
        try
        {
            stream.Position = position;
            var buffer = new byte[4];
            return ReadUpTo(stream, buffer, 4) < 4 ? null : ToUInt32BigEndian(buffer);
        }
        finally
        {
            stream.Position = origin;
        }
    }

    private static bool IsId3TagAt(Stream stream, long position)
    {
        var header = PeekAt(stream, position);
        return header is not null &&
               (header.Value >> 24 & 0xFF) == 'I' &&
               (header.Value >> 16 & 0xFF) == 'D' &&
               (header.Value >> 8 & 0xFF) == '3';
    }

    /// <summary>Position after the ID3v2 tag starting here, or -1 when there is no whole tag.</summary>
    private static long SkipId3TagAt(Stream stream, long position)
    {
        if (!IsId3TagAt(stream, position) || position + 10 > stream.Length) return -1;

        stream.Position = position;
        var header = new byte[10];
        if (ReadUpTo(stream, header, 10) < 10) return -1;

        // The synchsafe size covers everything after these ten bytes, footer included.
        var size = (header[6] & 0x7F) << 21 | (header[7] & 0x7F) << 14 | (header[8] & 0x7F) << 7 | header[9] & 0x7F;
        var end = position + 10 + size;

        return size <= 0 || end > stream.Length ? -1 : end;
    }

    private static int ReadUpTo(Stream stream, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var got = stream.Read(buffer, read, count - read);
            if (got <= 0) break;
            read += got;
        }

        return read;
    }

    private static uint ToUInt32BigEndian(byte[] buffer) =>
        (uint)buffer[0] << 24 | (uint)buffer[1] << 16 | (uint)buffer[2] << 8 | buffer[3];
}

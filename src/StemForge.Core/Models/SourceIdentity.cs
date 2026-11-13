using System.Security.Cryptography;
using System.Text;

namespace StemForge.Core.Models;

/// <summary>
/// Identity of a source audio file. A change to any part of it means the file is
/// new work rather than a resume of a previous job.
/// </summary>
public sealed record SourceIdentity(
    string Path,
    long SizeBytes,
    long LastWriteUtcTicks,
    string QuickHash)
{
    private const int SampleBytes = 64 * 1024;

    public static SourceIdentity Capture(string fullPath)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("Source audio file not found.", fullPath);

        return new SourceIdentity(
            info.FullName,
            info.Length,
            info.LastWriteTimeUtc.Ticks,
            ComputeQuickHash(info));
    }

    /// <summary>
    /// Cheap change detector: size plus the first and last 64 KB of the file.
    /// Not a full-file hash (files can be very large); sufficient to notice edits,
    /// replacements and truncation while staying fast for big batches.
    /// </summary>
    private static string ComputeQuickHash(FileInfo info)
    {
        using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA1.Create();

        var head = new byte[Math.Min(SampleBytes, (int)Math.Min(info.Length, int.MaxValue))];
        var read = stream.Read(head, 0, head.Length);
        sha.TransformBlock(head, 0, read, null, 0);

        if (info.Length > SampleBytes)
        {
            stream.Seek(Math.Max(0, info.Length - SampleBytes), SeekOrigin.Begin);
            var tail = new byte[SampleBytes];
            var tRead = stream.Read(tail, 0, tail.Length);
            sha.TransformBlock(tail, 0, tRead, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    /// <summary>Returns true when the file on disk no longer matches this identity.</summary>
    public bool HasChanged(out string reason)
    {
        var info = new FileInfo(Path);
        if (!info.Exists)
        {
            reason = "source file no longer exists";
            return true;
        }

        if (info.Length != SizeBytes)
        {
            reason = $"size changed ({SizeBytes} -> {info.Length} bytes)";
            return true;
        }

        if (info.LastWriteTimeUtc.Ticks != LastWriteUtcTicks)
        {
            reason = "last write time changed";
            return true;
        }

        var hash = ComputeQuickHash(info);
        if (!string.Equals(hash, QuickHash, StringComparison.OrdinalIgnoreCase))
        {
            reason = "file contents changed";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    public string ShortHash => QuickHash[..Math.Min(12, QuickHash.Length)];
}

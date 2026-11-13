using System.Security.Cryptography;
using System.Text;

namespace StemForge.Core.Models;

/// <summary>
/// Deterministic, stable job identity.
/// The id is derived from everything that defines the work: the source file's
/// identity (path + size + mtime + quick hash), the requested stems, the processing
/// mode and the destination. Consequences:
///   * restarting the app produces the same ids, so resume works;
///   * editing the source file produces a new id, so it is treated as new work;
///   * a Drums Only / High Quality job can never satisfy an All Stems / High Speed job.
/// </summary>
public static class JobId
{
    public static string Create(SourceIdentity source, JobProfile profile, string destinationRoot)
    {
        var canonicalDest = NormalizeDestination(destinationRoot);
        var material = string.Join('\u001f',
            source.Path.ToLowerInvariant(),
            source.SizeBytes.ToString(),
            source.LastWriteUtcTicks.ToString(),
            source.QuickHash,
            profile.Key,
            canonicalDest);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "job-" + Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }

    private static string NormalizeDestination(string destinationRoot)
    {
        var full = Path.GetFullPath(destinationRoot);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   .ToLowerInvariant();
    }
}

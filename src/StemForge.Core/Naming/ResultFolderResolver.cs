using StemForge.Core.Models;
using StemForge.Core.Publish;
using StemForge.Core.Support;

namespace StemForge.Core.Naming;

/// <summary>
/// Chooses the result folder for a job so that:
///   * the same track gets a readable folder ("Artist - Track");
///   * different stem selections, modes, or two different files that share a name never
///     land in the same folder;
///   * a retry of the same job reuses its own folder instead of creating a second one;
///   * pre-existing, unrelated folders are never hijacked.
/// </summary>
public static class ResultFolderResolver
{
    public static string Resolve(
        string destinationRoot,
        string trackBaseName,
        JobProfile profile,
        SourceIdentity source,
        out string folderName,
        string? previousResultFolder = null)
    {
        // If a previous attempt already published for this job, keep that exact folder.
        if (!string.IsNullOrEmpty(previousResultFolder))
        {
            var previousFull = Path.Combine(destinationRoot, previousResultFolder);
            if (CanUse(previousFull, profile, source))
            {
                folderName = previousResultFolder;
                return previousFull;
            }
        }

        var baseName = PathSanitizer.SanitizeFolderName(trackBaseName);
        var suffix = profile.FolderSuffix;
        var preferred = string.IsNullOrEmpty(suffix) ? baseName : $"{baseName} ({suffix})";

        var candidate = preferred;
        var counter = 2;
        while (!CanUse(Path.Combine(destinationRoot, candidate), profile, source))
        {
            candidate = $"{preferred} ({counter})";
            counter++;
        }

        folderName = candidate;
        return Path.Combine(destinationRoot, candidate);
    }

    /// <summary>
    /// A folder may be used when it does not exist yet, is empty, or holds a manifest
    /// for exactly this profile *and* this source. A folder belonging to a different
    /// track that happens to share the same name is left alone.
    /// </summary>
    private static bool CanUse(string folder, JobProfile profile, SourceIdentity source)
    {
        if (!Directory.Exists(folder)) return true;

        if (!Directory.EnumerateFileSystemEntries(folder).Any()) return true;

        var manifest = ManifestStore.Read(folder);
        if (manifest is null) return false;

        // A partial result is never reused and never overwritten by a complete one.
        if (manifest.Partial) return false;

        return string.Equals(manifest.ProfileKey, profile.Key, StringComparison.Ordinal)
            && manifest.Matches(profile, source);
    }
}

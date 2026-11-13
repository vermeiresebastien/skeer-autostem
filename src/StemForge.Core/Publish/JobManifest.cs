using StemForge.Core.Models;
using StemForge.Core.Support;

namespace StemForge.Core.Publish;

public sealed class ManifestFile
{
    public string Name { get; set; } = string.Empty;
    public string Stem { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
}

/// <summary>
/// Written into every published result folder. It records which job produced the
/// folder and what it contains, which is how the app proves a result belongs to the
/// current job, detects already-satisfied work on resume, and refuses to reuse a
/// High Quality / Drums Only result for an All Stems / High Speed request.
/// </summary>
public sealed class JobManifest
{
    public const string FileName = "autostem.manifest.json";

    public int Schema { get; set; } = 1;
    public string JobId { get; set; } = string.Empty;
    public string ProfileKey { get; set; } = string.Empty;
    public Stems Stems { get; set; }
    public ProcessingMode Mode { get; set; }

    public string SourcePath { get; set; } = string.Empty;
    public long SourceSizeBytes { get; set; }
    public long SourceLastWriteUtcTicks { get; set; }
    public string SourceQuickHash { get; set; } = string.Empty;

    /// <summary>False while files are still being moved in; flipped last so a crash mid-publish is detectable.</summary>
    public bool Complete { get; set; }

    /// <summary>
    /// True for a partial result: a verified subset salvaged from a failed attempt. A
    /// partial manifest never satisfies a job and is never overwritten by a complete one.
    /// </summary>
    public bool Partial { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    public string? LiveVersion { get; set; }

    public List<ManifestFile> Files { get; set; } = new();

    /// <summary>True when the manifest describes exactly the same work as the given profile + source.</summary>
    public bool Matches(JobProfile profile, SourceIdentity source) =>
        string.Equals(ProfileKey, profile.Key, StringComparison.Ordinal)
        && SourceSizeBytes == source.SizeBytes
        && SourceLastWriteUtcTicks == source.LastWriteUtcTicks
        && string.Equals(SourceQuickHash, source.QuickHash, StringComparison.OrdinalIgnoreCase)
        && string.Equals(SourcePath, source.Path, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the manifest is complete and every listed file still exists on disk.</summary>
    public bool IsIntact(string folder)
    {
        if (!Complete || Files.Count == 0) return false;
        foreach (var file in Files)
        {
            var path = Path.Combine(folder, file.Name);
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length != file.Bytes) return false;
        }
        return true;
    }
}

public static class ManifestStore
{
    public static string ManifestPath(string folder) => Path.Combine(folder, JobManifest.FileName);

    public static JobManifest? Read(string folder)
    {
        try
        {
            return JsonStore.Load<JobManifest>(ManifestPath(folder));
        }
        catch
        {
            return null;
        }
    }

    public static void Write(string folder, JobManifest manifest)
    {
        Directory.CreateDirectory(folder);
        JsonStore.Save(ManifestPath(folder), manifest);
    }
}

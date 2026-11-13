using StemForge.Core.Models;

namespace StemForge.Core.Live;

/// <summary>
/// Maps the audio files Live generated in its Stems folder onto the stems the job
/// requested. Live names its outputs after the stem ("Vocals", "Drums", ...), so
/// matching is keyword based and tolerant of Live's naming decorations.
/// </summary>
public static class StemFileMapper
{
    public static IReadOnlyDictionary<Stems, string> Map(
        Stems requested,
        IEnumerable<string> candidateFiles,
        string? clipName = null)
    {
        var files = candidateFiles.ToList();
        var result = new Dictionary<Stems, string>();

        foreach (var stem in requested.Enumerate())
        {
            var match = FindFor(stem, files);
            if (match is not null)
            {
                result[stem] = match;
                files.Remove(match);
            }
        }

        return result;
    }

    private static string? FindFor(Stems stem, IReadOnlyList<string> files)
    {
        // Order matters: "others" must not be matched by the "other" keyword of a
        // different stem, and "vocals" must win over a generic name.
        var keywords = stem switch
        {
            Stems.Vocals => new[] { "vocal", "vox", "vocalstem" },
            Stems.Drums => new[] { "drum", "perc" },
            Stems.Bass => new[] { "bass" },
            Stems.Others => new[] { "other", "instrumental", "remaining" },
            _ => Array.Empty<string>(),
        };

        foreach (var keyword in keywords)
        {
            var match = files.FirstOrDefault(f =>
                Path.GetFileName(f).Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return null;
    }

    /// <summary>True when the file name looks like one Live generated for a stem.</summary>
    public static bool LooksLikeStemFile(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith('.')) return false;
        return new[] { "vocal", "drum", "bass", "other", "perc" }
            .Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}

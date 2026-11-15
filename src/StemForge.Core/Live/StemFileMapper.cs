using StemForge.Core.Models;

namespace StemForge.Core.Live;

/// <summary>
/// Maps the audio files Live generated in its Stems folder onto the stems the job
/// requested. Live names its outputs after the stem ("Vocals", "Drums", ...), so
/// matching is keyword based and tolerant of Live's naming decorations.
///
/// Two things about Live's own naming are load-bearing here. The stem word is the
/// <em>last</em> parenthesised group of a name like
/// "<c>Akcept - Howl (Original Mix) [2026-11-14 031511] (Vocals).wav</c>" - matching on it
/// rather than anywhere in the string is what stops a track called "Vox Populi" from having
/// its drum stem handed over as its vocal stem. And Live writes a <c>.wav.asd</c> analysis
/// file for every stem, <em>after</em> writing the WAV, so a candidate list ordered newest
/// first leads with sidecars; anything that is not an audio container has to be excluded
/// before matching, not after.
/// </summary>
public static class StemFileMapper
{
    /// <summary>
    /// Containers <see cref="StemFormatConverter"/> can deliver as WAV. A stem in anything else
    /// is not collectable, and pretending otherwise turns a real separation into an error about
    /// a file that was never audio.
    /// </summary>
    private static readonly string[] AudioExtensions = [".wav", ".wave", ".aif", ".aiff", ".aifc"];

    public static bool IsAudioContainer(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<Stems, string> Map(
        Stems requested,
        IEnumerable<string> candidateFiles,
        string? clipName = null)
    {
        var files = candidateFiles.Where(IsAudioContainer).ToList();
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

        // The name Live gave this file, minus its extension and its trailing group:
        // "(Vocals)" out of "... (Vocals).wav". Null for any other naming scheme.
        string? Label(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.EndsWith(')')) return null;
            var open = name.LastIndexOf('(');
            return open < 0 ? null : name[(open + 1)..^1];
        }

        foreach (var keyword in keywords)
        {
            var labelled = files.FirstOrDefault(f =>
                Label(f)?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true);
            if (labelled is not null) return labelled;
        }

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
        if (!IsAudioContainer(path)) return false;

        var name = Path.GetFileName(path);
        if (name.StartsWith('.')) return false;
        return new[] { "vocal", "drum", "bass", "other", "perc" }
            .Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}

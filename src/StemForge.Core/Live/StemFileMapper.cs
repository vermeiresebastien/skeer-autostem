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

    /// <summary>
    /// Live's in-flight render target. Each stem is written as <c>AbletonTmp-000N</c> - no
    /// extension, and a RIFF header whose size field is still 72 - and renamed to the final
    /// "... (Bass).wav" only when the render is committed. Measured here on 2026-11-15: four of
    /// them, each about half its stem's length, sat size-stable for 2s while Live was still
    /// writing, and the job reported "no stem files were produced" over 33 MB of audio.
    /// </summary>
    public static bool IsLiveScratchFile(string path) =>
        Path.GetFileName(path).StartsWith("AbletonTmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>How big a file was and when it was last written, as far as one poll saw it.</summary>
    public readonly record struct FileStamp(long Size, DateTime LastWrite);

    /// <summary>
    /// What a render has produced so far: the deliverables, the targets still being written, and
    /// the targets that have stopped being written to without ever becoming deliverables.
    ///
    /// <see cref="Stalled"/> is the list a failing job has to name. A plateau of <c>AbletonTmp</c>
    /// files is Live saying the render is over without committing anything, and a watcher that only
    /// answers "is it still writing" treats that plateau as work in progress forever - which is how
    /// a run spent its whole timeout and then reported a timeout.
    /// </summary>
    public readonly record struct RenderOutput(
        IReadOnlyList<string> Committed,
        IReadOnlyList<string> Writing,
        IReadOnlyList<string> Stalled)
    {
        /// <summary>True while at least one render target is still advancing.</summary>
        public bool Committing => Writing.Count > 0;
    }

    /// <summary>
    /// Classifies a render with no history: every target is taken at its word and called writing.
    /// Only useful where there is no previous poll to compare against.
    /// </summary>
    public static RenderOutput ClassifyRender(IEnumerable<string> newOrChangedFiles) =>
        ClassifyRender(newOrChangedFiles, NoStamps, NoStamps, TimeSpan.Zero, DateTime.UtcNow);

    private static readonly IReadOnlyDictionary<string, FileStamp> NoStamps =
        new Dictionary<string, FileStamp>();

    /// <summary>
    /// Splits the files a render just wrote into deliverables, work in progress, and work that has
    /// stopped without finishing.
    ///
    /// Three different mistakes were made on this one line, so it is stated once and tested: the
    /// <c>.wav.asd</c> sidecars are not audio, Live's <c>AbletonTmp-000N</c> render targets are not
    /// finished stems, and audio that appears while a render target is still being written belongs
    /// to a render that has not committed yet.
    ///
    /// A fourth mistake was made on the third: "still being written" was answered by asking whether
    /// <em>any</em> target existed anywhere in the watched folders. That is folder-wide and binary,
    /// so one leftover from a crashed run pinned every later job's completion check - and a target
    /// of this job that stopped growing was indistinguishable from one that had not. A target counts
    /// as writing only while it is actually advancing: its size or write time moved since the
    /// previous poll, or Windows says it was touched inside <paramref name="stallPatience"/>.
    /// </summary>
    /// <param name="newOrChangedFiles">what this job has brought since its baseline</param>
    /// <param name="previousPoll">what the last poll saw, for the paths it saw</param>
    /// <param name="thisPoll">what this poll sees, for every path being classified</param>
    /// <param name="stallPatience">how long a target may stop advancing before that means something</param>
    public static RenderOutput ClassifyRender(
        IEnumerable<string> newOrChangedFiles,
        IReadOnlyDictionary<string, FileStamp> previousPoll,
        IReadOnlyDictionary<string, FileStamp> thisPoll,
        TimeSpan stallPatience,
        DateTime at)
    {
        var committed = new List<string>();
        var writing = new List<string>();
        var stalled = new List<string>();

        foreach (var path in newOrChangedFiles)
        {
            if (IsLiveScratchFile(path))
            {
                if (IsStalled(path, previousPoll, thisPoll, stallPatience, at)) stalled.Add(path);
                else writing.Add(path);
            }
            else if (IsAudioContainer(path))
            {
                committed.Add(path);
            }
        }

        return new RenderOutput(committed, writing, stalled);
    }

    /// <summary>
    /// True when a render target has stopped advancing: unchanged since the last poll, and last
    /// written at least <paramref name="stallPatience"/> ago. Both halves matter. A target first seen
    /// this poll has nothing to compare against and gets the benefit of the doubt, and a target
    /// measured here mid-render sat size-stable for about two seconds while Live was still writing
    /// it - so a plateau shorter than the patience is not yet a stalled render.
    /// </summary>
    private static bool IsStalled(
        string path,
        IReadOnlyDictionary<string, FileStamp> previousPoll,
        IReadOnlyDictionary<string, FileStamp> thisPoll,
        TimeSpan stallPatience,
        DateTime at)
    {
        if (!thisPoll.TryGetValue(path, out var now)) return false;
        if (!previousPoll.TryGetValue(path, out var before)) return false;
        if (before != now) return false;

        return at - now.LastWrite >= stallPatience;
    }

    /// <summary>
    /// One render target spelled out for the user: the name Live gave it, how much it holds and how
    /// long ago anything touched it.
    ///
    /// This is the sentence that replaces "separation timed out". A folder of <c>AbletonTmp-000N</c>
    /// files of identical size, untouched for two minutes, is evidence a person can act on - the
    /// render stopped, the audio is half-written, the set is still open - and a job that reports only
    /// a timeout throws that evidence away along with the files.
    ///
    /// <paramref name="at"/> is passed rather than read so the sentence can be tested; a target whose
    /// write time is ahead of the clock (skew, or a file Live has just renamed) says zero seconds
    /// rather than a negative one.
    /// </summary>
    public static string DescribeRenderTarget(string path, long sizeBytes, DateTime lastWrite, DateTime at) =>
        $"{RenderTargetName(path)} " +
        $"({sizeBytes / 1_048_576.0:F1} MB, last written {Math.Max(0, (at - lastWrite).TotalSeconds):F0}s ago)";

    /// <summary>The name alone, for a target whose size and write time could not be read.</summary>
    public static string RenderTargetName(string path) => Path.GetFileName(path);

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

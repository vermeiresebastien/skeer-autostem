namespace StemForge.Core.Live;

/// <summary>
/// Where Live's separated WAVs can turn out to be.
///
/// Live writes them under the project's own <c>Samples\Processed\Stems</c>. That is the answer
/// the automation asks for, and for a set Live treats as loose it is not where they land: Live
/// copies the set into <c>Documents\Ableton\Live Recordings\&lt;stamp&gt; Temp Project</c> and
/// generates the stems there. Measured with Live 12.3.5 on 2026-11-14, where the separation
/// succeeded — four real WAVs, 218 s apart from the click — and the folder being watched stayed
/// empty, so the job timed out on work Live had already done.
///
/// Both places are therefore watched. This is a separate type rather than a private helper
/// because the layout it has to match is Live's, not ours, and it is the one part of the run
/// that can be tested without an interactive desktop: the fixtures below are the directory names
/// Live actually produced.
/// </summary>
public static class LiveStemFolders
{
    /// <summary>The sub-path Live uses inside any project folder, loose or not.</summary>
    public static string RelativeStemsPath => Path.Combine("Samples", "Processed", "Stems");

    /// <summary>Where Live keeps the temporary projects it makes for sets it considers loose.</summary>
    public static string RecordingsRoot(string documentsFolder) =>
        Path.Combine(documentsFolder, "Ableton", "Live Recordings");

    /// <summary>
    /// The folders to snapshot before a separation and poll after it, in the order they should
    /// be searched: the job's own project first, then the newest of Live's temp projects.
    ///
    /// Temp projects are named <c>yyyy-MM-dd HHmmss Temp Project</c>, so their names sort in the
    /// same order as the moments they were made, and only the newest few are worth watching —
    /// a run leaves one behind, and a machine that has been used for a while has dozens.
    /// A temp project that does not exist yet still counts as watched, because the point of the
    /// baseline is that it is taken the same way as the poll: a folder appearing between the two
    /// is exactly the signal being looked for, and an older project left by an abandoned run
    /// cannot produce a new file without being the one Live is writing now.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string projectStemsFolder, string? recordingsRoot, int recentTempProjects = 3)
    {
        var folders = new List<string> { projectStemsFolder };

        if (recordingsRoot is null || !Directory.Exists(recordingsRoot))
            return folders;

        IEnumerable<string> temps;
        try
        {
            temps = Directory.EnumerateDirectories(recordingsRoot, "* Temp Project")
                             .OrderByDescending(Path.GetFileName)
                             .Take(Math.Max(0, recentTempProjects));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A directory Live is mid-way through creating can refuse to be listed. The job's own
            // folder is still watched, which is the case that matters when Live behaves.
            return folders;
        }

        foreach (var temp in temps)
        {
            var stems = Path.Combine(temp, RelativeStemsPath);
            if (Directory.Exists(stems)) folders.Add(stems);
        }

        return folders;
    }
}

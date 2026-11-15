using StemForge.Core.Live;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// Where the runner looks for the WAVs Live made. The directory and file names in these fixtures
/// are not invented: they are the ones Live 12.3.5 produced on 2026-11-14 during the run that
/// separated a track successfully while the runner watched an empty folder and timed out.
/// </summary>
public sealed class LiveStemFolderTests
{
    /// <summary>The stems folder of the project being watched, as Live writes it.</summary>
    private const string VocalsStem = "Akcept - Howl (Original Mix) [2026-11-14 031511] (Vocals).wav";

    private static string MakeTempProject(string recordingsRoot, string stamp, params string[] files)
    {
        var stems = Path.Combine(recordingsRoot, $"{stamp} Temp Project",
            "Samples", "Processed", "Stems");
        Directory.CreateDirectory(stems);
        foreach (var file in files) File.WriteAllText(Path.Combine(stems, file), "wav");
        return stems;
    }

    [Fact]
    public void LiveWritesStemsIntoATempProjectAndThatFolderIsWatched()
    {
        using var workspace = new TempWorkspace();
        var documents = workspace.NewFolder("Documents");
        var recordings = LiveStemFolders.RecordingsRoot(documents);
        var projectStems = Path.Combine(documents, "autoSTEM Working Set", "Samples", "Processed", "Stems");

        var liveWrote = MakeTempProject(recordings, "2026-11-14 041458", VocalsStem);

        var candidates = LiveStemFolders.Candidates(projectStems, recordings);

        Assert.Equal(projectStems, candidates[0]);
        Assert.Contains(liveWrote, candidates);
    }

    [Fact]
    public void TheRecordingsPathIsWhereLiveActuallyPutsThem()
    {
        Assert.Equal(
            Path.Combine("C:\\Users\\someone\\Documents", "Ableton", "Live Recordings"),
            LiveStemFolders.RecordingsRoot(Path.Combine("C:\\Users\\someone", "Documents")));
        Assert.Equal(Path.Combine("Samples", "Processed", "Stems"), LiveStemFolders.RelativeStemsPath);
    }

    private static string ExpectedStems(string recordingsRoot, string stamp) =>
        Path.Combine(recordingsRoot, $"{stamp} Temp Project", "Samples", "Processed", "Stems");

    [Fact]
    public void OnlyTheNewestTempProjectsAreWatchedAndTheyComeNewestFirst()
    {
        using var workspace = new TempWorkspace();
        var documents = workspace.NewFolder("Documents");
        var recordings = LiveStemFolders.RecordingsRoot(documents);
        var projectStems = Path.Combine(documents, "autoSTEM Working Set", "Samples", "Processed", "Stems");

        // Six launches, six temp projects - what the folder looks like after a night of runs.
        string[] stamps =
        [
            "2026-11-14 041236", "2026-11-14 041458", "2026-11-14 044812",
            "2026-11-14 045930", "2026-11-14 050144", "2026-11-14 050849",
        ];
        foreach (var stamp in stamps) MakeTempProject(recordings, stamp);

        var candidates = LiveStemFolders.Candidates(projectStems, recordings, recentTempProjects: 3);

        Assert.Equal(
            new[] { projectStems }.Concat(stamps.Reverse().Take(3).Select(stamp => ExpectedStems(recordings, stamp))),
            candidates);
    }

    [Fact]
    public void AFolderThatIsNotALiveProjectIsNotAStemCandidate()
    {
        using var workspace = new TempWorkspace();
        var documents = workspace.NewFolder("Documents");
        var recordings = LiveStemFolders.RecordingsRoot(documents);
        Directory.CreateDirectory(recordings);

        // An aborted project, an export, and a recording folder that only has the audio in it.
        Directory.CreateDirectory(Path.Combine(recordings, "2026-11-14 041458 Temp Project"));
        Directory.CreateDirectory(Path.Combine(recordings, "2026-11-13 221010 Audio Export"));
        Directory.CreateDirectory(Path.Combine(recordings, "notes"));

        var candidates = LiveStemFolders.Candidates(Path.Combine(documents, "set", "Stems"), recordings);

        Assert.Equal(Path.Combine(documents, "set", "Stems"), Assert.Single(candidates));
    }

    [Fact]
    public void AStemsFolderThatDoesNotExistYetIsStillTheFirstThingWatched()
    {
        // The baseline is taken the same way as the poll. If a folder that has not been created
        // yet were dropped from the list, the files appearing inside it would be invisible too.
        using var workspace = new TempWorkspace();
        var missing = Path.Combine(workspace.Path("Documents"), "autoSTEM Working Set", "Samples", "Processed", "Stems");

        var candidates = LiveStemFolders.Candidates(missing, LiveStemFolders.RecordingsRoot(workspace.Path("nope")));

        Assert.Equal(missing, Assert.Single(candidates));
    }

    [Fact]
    public void NoRecordingsFolderIsNotAFailure()
    {
        using var workspace = new TempWorkspace();
        var projectStems = workspace.NewFolder("set");

        Assert.Equal(projectStems, Assert.Single(LiveStemFolders.Candidates(projectStems, null)));
    }
}

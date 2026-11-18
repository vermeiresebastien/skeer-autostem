using StemForge.Core.Live;
using StemForge.Core.Models;
using Xunit;

namespace StemForge.Tests;

public sealed class StemFileMapperTests
{
    [Fact]
    public void PlainStemFileNamesAreMappedInOrder()
    {
        var files = new[] { "Vocals.wav", "Drums.wav", "Bass.wav", "Others.wav" };

        var mapped = StemFileMapper.Map(Stems.All, files);

        Assert.Equal(4, mapped.Count);
        Assert.EndsWith("Vocals.wav", mapped[Stems.Vocals]);
        Assert.EndsWith("Drums.wav", mapped[Stems.Drums]);
        Assert.EndsWith("Bass.wav", mapped[Stems.Bass]);
        Assert.EndsWith("Others.wav", mapped[Stems.Others]);
    }

    [Fact]
    public void PrefixedNamesAreMatchedByKeyword()
    {
        var files = new[] { "Artist - Track - Vocals.wav", "Artist - Track - Drums.wav" };

        var mapped = StemFileMapper.Map(Stems.Vocals | Stems.Drums, files);

        Assert.Equal(2, mapped.Count);
        Assert.Contains("Vocals", mapped[Stems.Vocals]);
        Assert.Contains("Drums", mapped[Stems.Drums]);
    }

    [Fact]
    public void OnlyRequestedStemsAreMapped()
    {
        var files = new[] { "Vocals.wav", "Drums.wav", "Bass.wav", "Others.wav" };

        var mapped = StemFileMapper.Map(Stems.Drums, files);

        Assert.Single(mapped);
        Assert.True(mapped.ContainsKey(Stems.Drums));
    }

    [Fact]
    public void OthersIsNotConfusedWithAnotherStem()
    {
        var files = new[] { "Drums.wav", "Others.wav" };

        var mapped = StemFileMapper.Map(Stems.All, files);

        Assert.False(mapped.ContainsKey(Stems.Vocals));
        Assert.EndsWith("Others.wav", mapped[Stems.Others]);
        Assert.EndsWith("Drums.wav", mapped[Stems.Drums]);
    }

    [Fact]
    public void NonStemFilesAreIgnored()
    {
        Assert.False(StemFileMapper.LooksLikeStemFile("readme.txt"));
        Assert.False(StemFileMapper.LooksLikeStemFile(".hidden.wav"));
        Assert.True(StemFileMapper.LooksLikeStemFile("Vocals.wav"));
    }

    /// <summary>
    /// The eight names below are Live 12.3.5's own, from the separation it completed at
    /// 03:18:49 UTC on 2026-11-14. The <c>.asd</c> analysis files were written *after* the WAVs
    /// - Live analyses the stems it just made - and the collector sorts candidates newest first,
    /// so a matcher that looks only for the stem word hands back a sidecar instead of audio.
    /// </summary>
    private static readonly string[] Track =
    [
        "Akcept - Howl (Original Mix) [2026-11-14 031511] (Vocals).wav",
        "Akcept - Howl (Original Mix) [2026-11-14 031511] (Drums).wav",
        "Akcept - Howl (Original Mix) [2026-11-14 031511] (Bass).wav",
        "Akcept - Howl (Original Mix) [2026-11-14 031511] (Others).wav",
    ];

    private static string[] WithSidecars() =>
        Track.Concat(Track.Select(name => name + ".asd")).ToArray();

    [Fact]
    public void ALiveAnalysisSidecarIsNotAStem()
    {
        Assert.False(StemFileMapper.LooksLikeStemFile(Track[0] + ".asd"));
        Assert.True(StemFileMapper.LooksLikeStemFile(Track[0]));
    }

    [Fact]
    public void LiveOwnNamesMapToLiveOwnAudioEvenWithTheSidecarsNewer()
    {
        // Newest first, which is what the collector hands over: sidecars lead the list.
        var candidates = WithSidecars().OrderByDescending(name => name.EndsWith(".asd")).ToArray();

        var mapped = StemFileMapper.Map(Stems.All, candidates);

        Assert.Equal(4, mapped.Count);
        foreach (var (stem, path) in mapped)
        {
            Assert.False(path.EndsWith(".asd"), $"{stem} was mapped to a Live analysis file");
            Assert.EndsWith($"({stem.DisplayName()}).wav", path);
        }
    }

    [Fact]
    public void ATrackNameContainingAStemWordCannotStealAnotherStemsFile()
    {
        // Every one of these mentions the vox, because the track is called that.
        var files = new[]
        {
            "Vox Populi [2026-11-14 031511] (Vocals).wav",
            "Vox Populi [2026-11-14 031511] (Drums).wav",
            "Vox Populi [2026-11-14 031511] (Bass).wav",
            "Vox Populi [2026-11-14 031511] (Others).wav",
        };

        var mapped = StemFileMapper.Map(Stems.All, files);

        Assert.Equal(4, mapped.Count);
        Assert.EndsWith("(Vocals).wav", mapped[Stems.Vocals]);
        Assert.EndsWith("(Drums).wav", mapped[Stems.Drums]);
        Assert.EndsWith("(Bass).wav", mapped[Stems.Bass]);
        Assert.EndsWith("(Others).wav", mapped[Stems.Others]);
    }

    // Live writes each stem into `AbletonTmp-000N` inside the project's Samples\Processed\Stems
    // and renames it to the "<clip> [stamp] (Stem).wav" form only when the render commits. The
    // four files below are the ones a killed render left behind in
    // "Documents\Ableton\Live Recordings\2026-11-15 182412 Temp Project\Samples\Processed\Stems":
    // 8,415,284 bytes each, size-stable for longer than the stability window, with a RIFF header
    // whose file-size field was still the empty-file 72. The run read that plateau as success,
    // logged "4 generated file(s) have been stable", and then collected nothing, because none of
    // the four is a file Live ever meant to hand out.
    private static readonly string[] LiveRenderTargets =
    [
        "AbletonTmp-0000",
        "AbletonTmp-0001",
        "AbletonTmp-0002",
        "AbletonTmp-0003",
    ];

    [Fact]
    public void LiveRenderTargetsAreNotStemsAndNeverAreCollected()
    {
        foreach (var target in LiveRenderTargets)
        {
            Assert.True(StemFileMapper.IsLiveScratchFile(target), $"{target} is a render target");
            Assert.False(StemFileMapper.IsAudioContainer(target), $"{target} has no container");
            Assert.False(StemFileMapper.LooksLikeStemFile(target), $"{target} is not a stem");
        }

        Assert.Empty(StemFileMapper.Map(Stems.All, LiveRenderTargets));
    }

    [Fact]
    public void APlateauOnRenderTargetsIsNotAFinishedRender()
    {
        var render = StemFileMapper.ClassifyRender(LiveRenderTargets);

        Assert.True(render.Committing);
        Assert.Empty(render.Committed);
    }

    [Fact]
    public void AudioThatLandsWhileARenderTargetIsOpenIsNotAFinishedRender()
    {
        // Live commits one stem at a time, so the first named WAV appears beside three targets.
        var render = StemFileMapper.ClassifyRender(
            ["Song 74 (Thic Mix) - fr4ctal [2025-12-24 094537] (Bass).wav", "AbletonTmp-0001"]);

        Assert.True(render.Committing);
        Assert.Single(render.Committed);
    }

    [Fact]
    public void OnlyCommittedAudioMakesARenderFinished()
    {
        var render = StemFileMapper.ClassifyRender(
            ["Cajmere feat. Dajae - Brighter Days (Underground Goodie Mix) myfreemp3.vip  [2025-12-24 063531] (Vocals).wav",
             "Cajmere feat. Dajae - Brighter Days (Underground Goodie Mix) myfreemp3.vip  [2025-12-24 063531] (Vocals).wav.asd"]);

        Assert.False(render.Committing);
        Assert.Single(render.Committed);
    }

    [Fact]
    public void TheNamesLiveActuallyCommittedToDiskMapToStems()
    {
        // Read out of two real temp projects; the second separated only two of the four stems.
        var bassAndVox = new[]
        {
            "Song 74 (Thic Mix) - fr4ctal [2025-12-24 094537] (Bass).wav",
            "Song 74 (Thic Mix) - fr4ctal [2025-12-24 094537] (Bass).wav.asd",
            "Song 74 (Thic Mix) - fr4ctal [2025-12-24 094537] (Vocals).wav",
            "Song 74 (Thic Mix) - fr4ctal [2025-12-24 094537] (Vocals).wav.asd",
        };

        var mapped = StemFileMapper.Map(Stems.All, bassAndVox);

        Assert.Equal(2, mapped.Count);
        Assert.EndsWith("(Vocals).wav", mapped[Stems.Vocals]);
        Assert.EndsWith("(Bass).wav", mapped[Stems.Bass]);
        Assert.False(mapped.ContainsKey(Stems.Drums));
        Assert.False(mapped.ContainsKey(Stems.Others));
    }

    /// <summary>
    /// The same plateau, told properly.
    ///
    /// "Is a render still going?" was answered by asking whether any <c>AbletonTmp</c> existed in the
    /// watched folders, which is folder-wide and binary: it cannot tell this job's target from a
    /// leftover of the crashed run above, so one stranded <c>AbletonTmp-0002</c> pinned every later
    /// job's completion check until its timeout, and the job died as a generic
    /// <c>SeparationTimeout</c> that named no file. What a watcher can actually say is whether a
    /// given target advanced since the last look.
    /// </summary>
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>How the two polls have to look for a target that stopped <paramref name="secondsAgo"/> ago.</summary>
    private static (
        IReadOnlyDictionary<string, StemFileMapper.FileStamp> Previous,
        IReadOnlyDictionary<string, StemFileMapper.FileStamp> Current) Plateau(
            string path, long size, double secondsAgo)
    {
        var stamp = new StemFileMapper.FileStamp(size, Now - TimeSpan.FromSeconds(secondsAgo));
        return (new Dictionary<string, StemFileMapper.FileStamp> { [path] = stamp },
                new Dictionary<string, StemFileMapper.FileStamp> { [path] = stamp });
    }

    [Fact]
    public void ARenderTargetThatStopsAdvancingIsStalledRatherThanCommitting()
    {
        // The measured shape of the killed render: ~8 MB, and nothing has touched it for over two
        // minutes. That is not a render in progress, and calling it one is what hid the failure.
        var (previous, current) = Plateau("AbletonTmp-0002", 8_415_284, 132);

        var render = StemFileMapper.ClassifyRender(
            ["AbletonTmp-0002"], previous, current, Patience, Now);

        Assert.False(render.Committing);
        Assert.Empty(render.Committed);
        Assert.Empty(render.Writing);
        Assert.Equal(new[] { "AbletonTmp-0002" }, render.Stalled);
    }

    [Fact]
    public void APlateauShorterThanThePatienceIsStillAWorkingRender()
    {
        // Live pauses mid-write: measured here at about two seconds of unchanged size while the
        // stems were still coming. Anything under the patience is a render, not a stranded file -
        // and a job that called this stalled would fail a separation that was about to finish.
        var (previous, current) = Plateau("AbletonTmp-0000", 8_415_284, 3);

        var render = StemFileMapper.ClassifyRender(
            ["AbletonTmp-0000"], previous, current, Patience, Now);

        Assert.True(render.Committing);
        Assert.Empty(render.Stalled);
    }

    [Fact]
    public void ATargetSeenForTheFirstTimeIsGivenTheBenefitOfTheDoubt()
    {
        // A leftover from an older run has a write time from whenever it died, and no previous poll
        // to compare it with. It is reported as working here, and the patience is what turns that
        // verdict into a stall on the next round - which is also the whole reason the classification
        // needs both polls rather than a directory listing.
        var current = new Dictionary<string, StemFileMapper.FileStamp>
        {
            ["AbletonTmp-0001"] = new(8_415_284, Now - TimeSpan.FromHours(3)),
        };

        var render = StemFileMapper.ClassifyRender(
            ["AbletonTmp-0001"],
            new Dictionary<string, StemFileMapper.FileStamp>(),
            current, Patience, Now);

        Assert.True(render.Committing);
        Assert.Empty(render.Stalled);
    }

    [Fact]
    public void ATargetThatGrewSinceTheLastPollIsWorkingWhateverItsWriteTimeLooksLike()
    {
        var previous = new Dictionary<string, StemFileMapper.FileStamp>
        {
            ["AbletonTmp-0003"] = new(4_000_000, Now - TimeSpan.FromMinutes(4)),
        };

        var current = new Dictionary<string, StemFileMapper.FileStamp>
        {
            ["AbletonTmp-0003"] = new(6_500_000, Now - TimeSpan.FromMinutes(4)),
        };

        var render = StemFileMapper.ClassifyRender(
            ["AbletonTmp-0003"], previous, current, Patience, Now);

        Assert.True(render.Committing);
        Assert.Empty(render.Stalled);
    }

    /// <summary>
    /// The consequence the timeout had, in one case: committed audio is collectable even while one
    /// target has stopped, so the job that used to die as "no stem files were produced" after its
    /// whole timeout now has its own answer - and its own name for the file in the way.
    /// </summary>
    [Fact]
    public void AStalledTargetDoesNotPinTheAudioThatDidLand()
    {
        var (previous, current) = Plateau("AbletonTmp-0002", 8_415_284, 90);

        var render = StemFileMapper.ClassifyRender(
            [
                "Akcept - Howl [2026-11-14 031511] (Vocals).wav",
                "Akcept - Howl [2026-11-14 031511] (Drums).wav",
                "AbletonTmp-0002",
            ],
            previous, current, Patience, Now);

        Assert.False(render.Committing);
        Assert.Equal(2, render.Committed.Count);
        Assert.Single(render.Stalled);
    }

    /// <summary>
    /// The no-history overload is what a caller with one listing gets, and it has to keep answering
    /// "not finished": with nothing to compare against, a target's only honest label is in progress.
    /// </summary>
    [Fact]
    public void ARenderWithNoHistoryNeverCallsATargetStalled()
    {
        var render = StemFileMapper.ClassifyRender(LiveRenderTargets, NoPoll, NoPoll, Patience, Now);

        Assert.True(render.Committing);
        Assert.Empty(render.Stalled);
        Assert.Equal(4, render.Writing.Count);
    }

    private static readonly IReadOnlyDictionary<string, StemFileMapper.FileStamp> NoPoll =
        new Dictionary<string, StemFileMapper.FileStamp>();

    /// <summary>
    /// The other half of naming a stuck render: the words the job dies with. A timeout on its own
    /// sends the user to the folder to find out which of four identically sized <c>AbletonTmp</c>
    /// files Live abandoned, at a size that says how far it got.
    /// </summary>
    [Fact]
    public void AStalledRenderTargetIsNamedWithItsSizeAndHowLongItHasSat()
    {
        var written = Now - TimeSpan.FromSeconds(132);

        Assert.Equal(
            "AbletonTmp-0002 (8.0 MB, last written 132s ago)",
            StemFileMapper.DescribeRenderTarget(
                @"C:\Users\seb\Documents\Ableton\Live Recordings\2026-11-15 182412 Temp Project" +
                @"\Samples\Processed\Stems\AbletonTmp-0002",
                8_415_284, written, Now));

        // A clock that disagrees with the file has nothing useful to say, and a negative age is
        // worse than no age: it reads as a render that has not started yet.
        Assert.Contains("last written 0s ago", StemFileMapper.DescribeRenderTarget(
            "AbletonTmp-0000", 1024, Now + TimeSpan.FromMinutes(5), Now));

        // And a target that could not be stated has to be named anyway.
        Assert.Equal("AbletonTmp-0003", StemFileMapper.RenderTargetName(@"D:\x\AbletonTmp-0003"));
    }
}

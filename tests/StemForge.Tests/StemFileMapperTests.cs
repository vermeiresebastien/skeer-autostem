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
}

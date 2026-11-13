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
}

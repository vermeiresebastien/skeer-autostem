using StemForge.Core.Models;
using StemForge.Core.Naming;
using StemForge.Core.Publish;
using StemForge.Core.Support;
using Xunit;

namespace StemForge.Tests;

public sealed class NamingTests
{
    [Theory]
    [InlineData("Artist - Track", "Artist - Track")]
    [InlineData("Bad/Name:With*Chars?", "Bad_Name_With_Chars_")]
    [InlineData("trailing dots...   ", "trailing dots")]
    [InlineData("CON", "CON_")]
    [InlineData("lpt1", "lpt1_")]
    public void FolderNamesAreAlwaysWindowsSafe(string input, string expected)
    {
        Assert.Equal(expected, PathSanitizer.SanitizeFolderName(input));
    }

    [Fact]
    public void VeryLongNamesAreTruncated()
    {
        var sanitized = PathSanitizer.SanitizeFolderName(new string('a', 400));

        Assert.InRange(sanitized.Length, 1, PathSanitizer.MaxFolderNameLength);
    }

    [Fact]
    public void EmptyNamesFallBackToAPlaceholder()
    {
        Assert.Equal("Untitled", PathSanitizer.SanitizeFolderName("   "));
        Assert.Equal("Untitled", PathSanitizer.SanitizeFolderName(""));
    }

    [Fact]
    public void TheDefaultProfileGetsAPlainFolderName()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");

        var folder = ResultFolderResolver.Resolve(
            destination, "Artist - Track", JobProfile.Default, Source(workspace, "a.wav"), out var name);

        Assert.Equal("Artist - Track", name);
        Assert.Equal(Path.Combine(destination, "Artist - Track"), folder);
    }

    [Fact]
    public void ADifferentStemSelectionGetsItsOwnFolder()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = Source(workspace, "a.wav");

        PublishStub(destination, "Artist - Track", JobProfile.Default, source);

        var folder = ResultFolderResolver.Resolve(
            destination, "Artist - Track", new JobProfile(Stems.Drums, ProcessingMode.HighSpeed), source, out var name);

        Assert.NotEqual("Artist - Track", name);
        Assert.Contains("Drums", name);
        Assert.NotEqual(Path.Combine(destination, "Artist - Track"), folder);
    }

    [Fact]
    public void ADifferentModeGetsItsOwnFolder()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var source = Source(workspace, "a.wav");

        PublishStub(destination, "Artist - Track", new JobProfile(Stems.All, ProcessingMode.HighSpeed), source);

        var folder = ResultFolderResolver.Resolve(
            destination, "Artist - Track", new JobProfile(Stems.All, ProcessingMode.HighQuality), source, out var name);

        Assert.Contains("High Quality", name);
        Assert.NotEqual(Path.Combine(destination, "Artist - Track"), folder);
    }

    [Fact]
    public void TwoDifferentFilesWithTheSameNameGetDistinctFolders()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");

        var first = Source(workspace, "a.wav");
        var second = Source(workspace, "b.wav");

        var firstFolder = ResultFolderResolver.Resolve(destination, "Track", JobProfile.Default, first, out var firstName);
        PublishStub(destination, "Track", JobProfile.Default, first);

        var secondFolder = ResultFolderResolver.Resolve(destination, "Track", JobProfile.Default, second, out var secondName);

        Assert.Equal("Track", firstName);
        Assert.NotEqual(firstName, secondName);
        Assert.NotEqual(firstFolder, secondFolder);
    }

    [Fact]
    public void TheSameJobReusesItsOwnFolder()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");
        var profile = new JobProfile(Stems.Drums, ProcessingMode.HighQuality);
        var source = Source(workspace, "a.wav");

        PublishStub(destination, "Track", profile, source);

        var again = ResultFolderResolver.Resolve(destination, "Track", profile, source, out var name);

        Assert.Equal($"Track ({profile.FolderSuffix})", name);
        Assert.Single(Directory.GetDirectories(destination));
    }

    [Fact]
    public void AForeignFolderIsNeverHijacked()
    {
        using var workspace = new TempWorkspace();
        var destination = workspace.NewFolder("out");

        // A folder the user already had, with no StemForge manifest in it.
        var existing = Path.Combine(destination, "Track");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "my own file.txt"), "hands off");

        var folder = ResultFolderResolver.Resolve(
            destination, "Track", JobProfile.Default, Source(workspace, "a.wav"), out var name);

        Assert.NotEqual("Track", name);
        Assert.NotEqual(existing, folder);
        Assert.True(File.Exists(Path.Combine(existing, "my own file.txt")));
    }

    private static SourceIdentity Source(TempWorkspace workspace, string name) =>
        SourceIdentity.Capture(workspace.CreateWav(name, seconds: 0.25));

    /// <summary>Writes a manifest so the folder looks like a completed result for that profile.</summary>
    private static void PublishStub(string destination, string trackName, JobProfile profile, SourceIdentity source)
    {
        var folder = ResultFolderResolver.Resolve(destination, trackName, profile, source, out _);
        Directory.CreateDirectory(folder);
        ManifestStore.Write(folder, new JobManifest
        {
            JobId = "stub",
            ProfileKey = profile.Key,
            Stems = profile.Stems,
            Mode = profile.Mode,
            SourcePath = source.Path,
            SourceSizeBytes = source.SizeBytes,
            SourceLastWriteUtcTicks = source.LastWriteUtcTicks,
            SourceQuickHash = source.QuickHash,
            Complete = true,
        });
    }
}

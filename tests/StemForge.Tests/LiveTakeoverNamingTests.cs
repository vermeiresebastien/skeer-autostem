using StemForge.Core.Live;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The naming and busy-detection rules a takeover runs on. Both are pure, and both decide
/// whether an automatic close of somebody's Live session writes a file or loses one.
/// </summary>
public sealed class LiveTakeoverNamingTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 14, 3, 6, 15, TimeSpan.FromHours(1));

    [Theory]
    [InlineData("My Song.als - Ableton Live 12 Suite", "My Song")]
    [InlineData("Untitled* - Ableton Live 12 Suite", "Untitled")]
    [InlineData("autoSTEM Working Set* - Ableton Live 12 Suite", "autoSTEM Working Set")]
    [InlineData("", "Live Set")]
    [InlineData("   ", "Live Set")]
    [InlineData("Ableton Live 12 Suite", "Ableton Live 12 Suite")]
    public void TheProjectPartComesFromTheWindowTitle(string title, string expected)
    {
        Assert.Equal(expected, LiveSessionTakeover.ProjectNameFromTitle(title));
    }

    [Theory]
    [InlineData(@"Broken/Name:With*Chars?.als - Ableton Live 12 Suite")]
    [InlineData("Two   Spaces - Ableton Live 12 Suite")]
    [InlineData("...Leading Dots - Ableton Live 12 Suite")]
    public void TheProjectPartIsAlwaysUsableAsAFileName(string title)
    {
        var name = LiveSessionTakeover.ProjectNameFromTitle(title);

        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), name);
        Assert.DoesNotContain("  ", name);
        Assert.False(name.EndsWith('.'));
    }

    [Fact]
    public void ARecoveryNameCarriesTheMomentItWasTaken()
    {
        var name = LiveSessionTakeover.RecoveryName("Untitled* - Ableton Live 12 Suite", Stamp);

        Assert.Equal($"Untitled-{Stamp:yyyyMMdd-HHmmss}", name);
    }

    [Fact]
    public void ALongProjectNameIsShortenedInsteadOfTheStamp()
    {
        // The stamp is what keeps two takeovers apart, so the project name is the part that gives.
        var title = new string('x', 120) + " - Ableton Live 12 Suite";
        var name = LiveSessionTakeover.RecoveryName(title, Stamp);

        Assert.Equal(40 + 1 + "yyyyMMdd-HHmmss".Length, name.Length);
        Assert.EndsWith(Stamp.ToString("yyyyMMdd-HHmmss"), name);
    }

    [Fact]
    public void ARecoveryPathNeverReplacesSomethingThatAlreadyExists()
    {
        using var workspace = new TempWorkspace();
        var folder = workspace.NewFolder("recovered");

        var first = LiveSessionTakeover.RecoveryPath("Untitled - Ableton Live", folder, Stamp);
        File.WriteAllText(first, string.Empty);

        var second = LiveSessionTakeover.RecoveryPath("Untitled - Ableton Live", folder, Stamp);

        Assert.NotEqual(first, second);
        Assert.EndsWith("-2.als", second);
    }

    [Fact]
    public void ALiveSetFolderCountsAsTakingTheNameToo()
    {
        using var workspace = new TempWorkspace();
        var folder = workspace.NewFolder("recovered");

        var first = LiveSessionTakeover.RecoveryPath("My Song - Ableton Live", folder, Stamp);

        // Live saves a set as a folder named "<set>.als", so the file check alone would let a
        // second save collide with the first set's own directory.
        Directory.CreateDirectory(first);

        var second = LiveSessionTakeover.RecoveryPath("My Song - Ableton Live", folder, Stamp);

        Assert.NotEqual(first, second);
        Assert.EndsWith("-2.als", second);
    }

    [Fact]
    public void TheRecoveryFolderIsCreatedRatherThanLeftForTheDialogToFailOn()
    {
        using var workspace = new TempWorkspace();
        var folder = Path.Combine(workspace.Root, "does", "not", "exist", "yet");

        var path = LiveSessionTakeover.RecoveryPath("Untitled - Ableton Live", folder, Stamp);

        Assert.True(Directory.Exists(folder));
        Assert.StartsWith(folder, path);
    }

    [Theory]
    [InlineData("Analysis: Scanning (8,000 found)")]      // measured, from the run that stalled
    [InlineData("Analysis: Creating Loop Points")]
    [InlineData("Warping: 12 files")]
    [InlineData("Collecting and Saving...")]
    [InlineData("Exporting Audio")]
    public void LiveSaysItIsBusyInItsStatusLine(string status)
    {
        Assert.True(LiveAutomationRunner.IsLiveBusy(status));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1 Track selected")]
    [InlineData("Ready")]
    [InlineData("Session View")]
    [InlineData("Analysis: Paused")]                        // measured, after pressing the pause control
    public void AnIdleLiveIsNotBusy(string status)
    {
        Assert.False(LiveAutomationRunner.IsLiveBusy(status));
    }
}

using StemForge.Core.Live;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// Which Ableton Live a machine has more than one of. Both directories are complete installs and
/// the update-staging one is usually the newer, so "newest wins" picked the copy that spends its
/// first minutes re-importing the Core Library with its main window disabled.
///
/// Below that, what a located install can honestly claim about its edition - the input the support
/// gate refuses on, and a signal Ableton does not put in its own version info.
/// </summary>
public sealed class LiveLocatorTests
{
    [Theory]
    [InlineData(@"C:\ProgramData\Ableton\.Live 12 Suite_updated\Program\Ableton Live 12 Suite.exe", true)]
    [InlineData(@"C:\ProgramData\Ableton\Live 12 Suite_updated\Program\Ableton Live 12 Suite.exe", true)]
    [InlineData(@"C:\ProgramData\Ableton\.Live 11 Suite\Program\Ableton Live 11 Suite.exe", true)]
    public void AbletonsUpdateStagingCopyIsRecognised(string path, bool expected) =>
        Assert.Equal(expected, LiveLocator.IsUpdateStagingPath(path));

    [Theory]
    [InlineData(@"C:\ProgramData\Ableton\Live 12 Suite\Program\Ableton Live 12 Suite.exe")]
    [InlineData(@"C:\Program Files\Ableton\Live 12.3.2\Program\Ableton Live.exe")]
    [InlineData(@"D:\Live 12 Suite\Program\Ableton Live 12 Suite.exe")]
    [InlineData(@"C:\Users\Sebastien\Desktop\.Live Tools\helper.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void ANormalInstallIsNotAStagingCopy(string? path) =>
        Assert.False(LiveLocator.IsUpdateStagingPath(path!));

    [Fact]
    public void AStagingCopyOnAnyShareIsStillAStagingCopy() =>
        Assert.True(LiveLocator.IsUpdateStagingPath(
            @"\\nas\Ableton\.Live 12 Suite_updated\Program\Ableton Live 12 Suite.exe"));

    [Fact]
    public void TheInstallTheUserActuallyRunsWinsEvenWhenTheStagingCopyIsNewer()
    {
        var staging = new LiveInstall(
            @"C:\ProgramData\Ableton\.Live 12 Suite_updated\Program\Ableton Live 12 Suite.exe", "12.3.5", "Suite");
        var normal = new LiveInstall(
            @"C:\ProgramData\Ableton\Live 12 Suite\Program\Ableton Live 12 Suite.exe", "12.3.2", "Suite");

        var ranked = LiveLocator.Rank([staging, normal]);

        Assert.Equal(normal.ExePath, ranked[0].ExePath);
        Assert.Equal(staging.ExePath, ranked[1].ExePath);
    }

    [Fact]
    public void AStagingCopyIsStillUsedWhenItIsTheOnlyLiveOnTheMachine()
    {
        // Refusing to run because the folder has a dot in front of it would be worse than the
        // import it has to sit through.
        var staging = new LiveInstall(
            @"C:\ProgramData\Ableton\.Live 12 Suite_updated\Program\Ableton Live 12 Suite.exe", "12.3.5", "Suite");

        var ranked = LiveLocator.Rank([staging]);

        var only = Assert.Single(ranked);
        Assert.Equal(staging.ExePath, only.ExePath);
    }

    [Fact]
    public void BetweenTwoNormalInstallsTheNewerBuildStillWins()
    {
        // Both of these are builds the product can actually drive. Ranking a pair the support gate
        // would refuse anyway tests the sorter while pretending to describe a real machine.
        var older = new LiveInstall(@"C:\A\Live 11 Suite\Program\Live.exe", "11.3.8", "Suite");
        var newer = new LiveInstall(@"C:\A\Live 12 Suite\Program\Live.exe", "12.3.2", "Suite");

        Assert.Equal(newer.ExePath, LiveLocator.Rank([older, newer])[0].ExePath);
    }

    // ------------------------------------------------------------------- edition resolution
    //
    // The support gate refuses on the edition, so what the locator can and cannot tell about it is a
    // product promise, not a detail. Measured on this machine 2026-11-16: Ableton's version info says
    // FileVersion 12.3.2 / ProductName "Live" for the Suite install, and the same ProductName for the
    // 12.3.5 staging copy - the edition is in the folder and file names and nowhere else.

    [Theory]
    // The shape this desk actually has: ProductName carries no edition, the path does.
    [InlineData("Live", @"C:\ProgramData\Ableton\Live 12 Suite\Program\Ableton Live 12 Suite.exe", "Suite")]
    [InlineData("Live", @"C:\ProgramData\Ableton\.Live 12 Suite_updated\Program\Ableton Live 12 Suite.exe", "Suite")]
    // The route that would work if Ableton ever named the product in its version info.
    [InlineData("Live 12 Suite", @"C:\A\Live 12\Program\Ableton Live.exe", "Suite")]
    // The editions the support gate refuses, when the install says so.
    [InlineData("Live", @"C:\ProgramData\Ableton\Live 12 Standard\Program\Ableton Live 12 Standard.exe", "Standard")]
    [InlineData("Live", @"D:\Programs\Live 12 Intro\Program\Ableton Live 12 Intro.exe", "Intro")]
    [InlineData("Live", @"D:\Programs\Live 12 Lite\Program\Ableton Live 12 Lite.exe", "Lite")]
    // A decoy word beside the real edition name does not hide it: "Satellite" is not the edition,
    // the folder that says "Live 12 Lite" is.
    [InlineData("Live", @"D:\Samples\Satellite\Live 12 Lite\Program\Ableton Live 12 Lite.exe", "Lite")]
    // Nothing anywhere names an edition: this is the honest unknown, and the gate accepts it.
    [InlineData("Live", @"D:\Tools\Live 12\Program\Ableton Live.exe", "Live")]
    [InlineData(null, null, "Live")]
    [InlineData("", "", "Live")]
    public void TheEditionComesFromTheNameTheInstallCarries(string? product, string? exePath, string expected) =>
        Assert.Equal(expected, LiveLocator.ResolveEdition(product, exePath));

    [Theory]
    // A folder that merely contains an edition word is not an edition. Since the floor became a gate
    // this is load-bearing rather than cosmetic: a resolved Standard, Intro or Lite is a refusal, so
    // "Satellite Sessions" used to be able to stop somebody's batch.
    [InlineData(@"D:\Samples\Satellite Sessions\Live 12\Program\Ableton Live.exe")]
    [InlineData(@"D:\Samples\Standards Library\Live 12\Program\Ableton Live.exe")]
    [InlineData(@"D:\Courses\Introduction to Production\Live 12\Program\Ableton Live.exe")]
    [InlineData(@"D:\Sounds\SuiteB\Live 12\Program\Ableton Live.exe")]
    public void AFolderThatMerelyContainsAnEditionWordIsNotAnEdition(string exePath) =>
        Assert.Equal(LiveInstall.UnknownEdition, LiveLocator.ResolveEdition("Live", exePath));

    [Fact]
    public void TheRealShapeOfThisMachineIsResolvedAndAccepted()
    {
        const string exe = @"C:\ProgramData\Ableton\Live 12 Suite\Program\Ableton Live 12 Suite.exe";

        var install = new LiveInstall(exe, "12.3.2", LiveLocator.ResolveEdition("Live", exe));

        Assert.Equal("Suite", install.Edition);
        Assert.True(install.IsSupported);
        Assert.Equal("Ableton Live 12.3.2 Suite", install.DisplayName);
    }

    [Fact]
    public void AnInstallThatNamesNoEditionIsNeitherConfirmedNorClaimed()
    {
        const string exe = @"D:\Tools\Live 12\Program\Ableton Live.exe";

        var install = new LiveInstall(exe, "12.3.2", LiveLocator.ResolveEdition("Live", exe));

        Assert.Equal(LiveInstall.UnknownEdition, install.Edition);
        Assert.True(install.IsSupported);
        Assert.Equal("Ableton Live 12.3.2 edition not detected", install.DisplayName);
    }
}

using StemForge.Core.Live.Automation;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// What the run is allowed to do with the screen while Live renders. Getting this wrong in one
/// direction leaves Live holding the display for minutes it does not need it; getting it wrong
/// in the other steals a window the user has already taken back.
/// </summary>
public sealed class ScreenYieldTests
{
    private static ScreenYieldFacts Facts(
        bool exists = true,
        bool visible = true,
        bool ownerIsLive = false,
        bool ownerIsForeground = false,
        bool foregroundIsOursOrLive = true) =>
        new(exists, visible, ownerIsLive, ownerIsForeground, foregroundIsOursOrLive);

    [Fact]
    public void TheScreenGoesBackToTheWindowThatHadItBeforeTheRunStarted()
    {
        Assert.Equal(ScreenYieldPlan.HandBack, ScreenYield.Plan(Facts()));
    }

    [Fact]
    public void AWindowThatHasSinceClosedIsNotActivated()
    {
        Assert.Equal(ScreenYieldPlan.NothingToHandBack, ScreenYield.Plan(Facts(exists: false)));
    }

    [Fact]
    public void AWindowTheUserMinimisedIsNotRestored()
    {
        // Visible is checked before anything else about the owner: activating a minimised window
        // puts it back on screen, which is not what putting the screen "back" means.
        Assert.Equal(ScreenYieldPlan.OwnerHasGoneAway,
            ScreenYield.Plan(Facts(visible: false, ownerIsForeground: true)));
    }

    [Fact]
    public void LiveHoldingTheScreenBeforeTheRunIsNothingToHandBack()
    {
        Assert.Equal(ScreenYieldPlan.LiveAlreadyOwnsTheScreen, ScreenYield.Plan(Facts(ownerIsLive: true)));
    }

    [Fact]
    public void AnAlreadyVisibleWindowIsNotRaisedAgain()
    {
        Assert.Equal(ScreenYieldPlan.AlreadyHandedBack, ScreenYield.Plan(Facts(ownerIsForeground: true)));
    }

    [Fact]
    public void AWindowTheUserHasTakenIsNotTakenBack()
    {
        // The run may not fight the user for the foreground; that is a person deciding to work.
        Assert.Equal(ScreenYieldPlan.UserHasTheScreen,
            ScreenYield.Plan(Facts(foregroundIsOursOrLive: false)));
    }

    [Fact]
    public void TheUsersWindowOutranksEverythingExceptBeingGone()
    {
        Assert.Equal(ScreenYieldPlan.OwnerHasGoneAway,
            ScreenYield.Plan(Facts(visible: false, ownerIsLive: true, foregroundIsOursOrLive: false)));

        Assert.Equal(ScreenYieldPlan.LiveAlreadyOwnsTheScreen,
            ScreenYield.Plan(Facts(ownerIsLive: true, foregroundIsOursOrLive: false)));
    }
}

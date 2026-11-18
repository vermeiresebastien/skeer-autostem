using StemForge.Core.Live.Automation;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The arithmetic behind a click. MOUSEEVENTF_ABSOLUTE does not take pixels: it takes a position
/// normalised over the whole virtual desktop, and passing pixels instead divides the target by
/// the screen size. Measured on the machine this runs on - two stacked 1920x1080 monitors, so a
/// 1920x2160 virtual desktop - a click aimed at Live's "Create" entry at (91,34) arrived at
/// (2,0), which is Live's system-menu icon, and that is where every coordinate click in this
/// product has ever landed.
/// </summary>
public sealed class InputSimulatorTests
{
    private const int PrimaryWidth = 1920;
    private const int StackedHeight = 2160;

    /// <summary>Where Windows would actually put a cursor for the units SendInput was given.</summary>
    private static (int X, int Y) Decode(int dx, int dy, int left, int top, int width, int height) =>
        ((int)Math.Round(dx * (width - 1) / 65535.0) + left, (int)Math.Round(dy * (height - 1) / 65535.0) + top);

    [Fact]
    public void APixelPositionIsConvertedToSendInputUnitsOverTheWholeVirtualDesktop()
    {
        var (dx, dy) = InputSimulator.Normalize(91, 34, 0, 0, PrimaryWidth, StackedHeight);

        Assert.Equal((91, 34), Decode(dx, dy, 0, 0, PrimaryWidth, StackedHeight));
    }

    [Fact]
    public void PassingPixelsInsteadOfUnitsAimsAtTheTopLeftCorner()
    {
        // What the code did before it normalised: hand SendInput the pixel pair unchanged.
        var (x, y) = Decode(91, 34, 0, 0, PrimaryWidth, StackedHeight);

        Assert.True(x < 5 && y < 5, $"pixels passed as units landed at ({x},{y}); the target was (91,34)");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(91, 34)]
    [InlineData(600, 700)]
    [InlineData(1919, 2159)]
    [InlineData(500, 1500)]
    public void EveryPointOfAStackedDesktopIsReachable(int x, int y)
    {
        var (dx, dy) = InputSimulator.Normalize(x, y, 0, 0, PrimaryWidth, StackedHeight);

        Assert.Equal((x, y), Decode(dx, dy, 0, 0, PrimaryWidth, StackedHeight));
    }

    [Fact]
    public void ASecondMonitorBelowThePrimaryIsNotFoldedOntoTheFirst()
    {
        var below = InputSimulator.Normalize(500, 1500, 0, 0, PrimaryWidth, StackedHeight);
        var onPrimary = InputSimulator.Normalize(500, 300, 0, 0, PrimaryWidth, StackedHeight);

        Assert.True(below.Dy > onPrimary.Dy, "a point on the lower monitor must normalise above a primary one");
        Assert.Equal((500, 1500), Decode(below.Dx, below.Dy, 0, 0, PrimaryWidth, StackedHeight));
    }

    [Fact]
    public void AVirtualDesktopThatStartsOffscreenIsMeasuredFromItsOwnOrigin()
    {
        // A monitor to the left of the primary one makes the virtual desktop start at -1920, and
        // the primary screen is then the right half of it.
        var (dx, dy) = InputSimulator.Normalize(0, 0, -1920, 0, 3840, 1080);

        Assert.Equal((0, 0), Decode(dx, dy, -1920, 0, 3840, 1080));
        Assert.InRange(dx, 32700, 32800);
    }

    [Fact]
    public void TheBottomRightPixelOfTheDesktopIsStillInsideTheRange()
    {
        var (dx, dy) = InputSimulator.Normalize(1919, 2159, 0, 0, PrimaryWidth, StackedHeight);

        Assert.Equal(65535, dx);
        Assert.Equal(65535, dy);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1920, 1)]
    [InlineData(0, 2160)]
    public void ADesktopSmallerThanAPixelIsRefusedRatherThanDividedBy(int width, int height)
    {
        Assert.Throws<InvalidOperationException>(
            () => InputSimulator.Normalize(91, 34, 0, 0, width, height));
    }
}

using StemForge.Core.Live;
using StemForge.Core.Live.Automation;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// How the Create menu gets opened, and how a coordinate fallback gets aimed.
///
/// Both halves of this used to be believed on the automation's own word. Live answers a UIA
/// Invoke or ExpandCollapse with success whether or not anything opened, so a no-op stopped the
/// run from trying anything else and then waited out the whole dialog timeout for a popup that
/// had never been asked for. Separately, the window-relative point aimed ~8px above Live's menu
/// bar, into its caption, so the fallback could not work either.
/// </summary>
public sealed class CreateMenuChooserTests
{
    private sealed class Recorder
    {
        public List<string> Tried { get; } = [];
        public List<string> Notes { get; } = [];
        public int Presses { get; private set; }

        public Action Attempt(string name) => () => Tried.Add(name);

        public bool Press()
        {
            Presses++;
            return true;
        }

        public bool FailToPress()
        {
            Presses++;
            return false;
        }
    }

    [Fact]
    public void AnAttemptThatClaimsSuccessButOpensNothingIsFollowedByTheNextAttempt()
    {
        var seen = 0;
        var recorder = new Recorder();
        var attempts = new[]
        {
            ("the menu pattern", recorder.Attempt("pattern")),
            ("Live's own rectangle", recorder.Attempt("rectangle")),
        };

        // The pattern claims to have worked; only the desktop knows it did not.
        var result = CreateMenuChooser.Choose(
            attempts,
            () => ++seen == 1 ? MenuPopupState.NotPoppedUp : MenuPopupState.Pressable,
            recorder.Press,
            recorder.Notes.Add);

        Assert.Equal(MenuChoiceResult.Chosen, result);
        Assert.Equal("pattern,rectangle", string.Join(",", recorder.Tried));
        Assert.Contains("'the menu pattern' did not bring up the Create menu", recorder.Notes);
    }

    [Fact]
    public void AMenuThatOpensAndClosesAgainIsOpenedAgainRatherThanPressedInGhost()
    {
        var seen = 0;
        var recorder = new Recorder();
        var attempts = new[]
        {
            ("first", recorder.Attempt("first")),
            ("second", recorder.Attempt("second")),
        };

        var result = CreateMenuChooser.Choose(
            attempts,
            () => ++seen == 1 ? MenuPopupState.ClosedAgain : MenuPopupState.Pressable,
            recorder.Press,
            recorder.Notes.Add);

        Assert.Equal(MenuChoiceResult.Chosen, result);
        Assert.Equal("first,second", string.Join(",", recorder.Tried));
        Assert.Contains("'first' opened the Create menu and it closed again", recorder.Notes);
    }

    [Fact]
    public void AGreyedOutEntryIsReportedAsRefusalAndNeverPressed()
    {
        var recorder = new Recorder();
        var attempts = new[] { ("only", recorder.Attempt("only")) };

        var result = CreateMenuChooser.Choose(
            attempts,
            () => MenuPopupState.GreyedOut,
            recorder.Press,
            recorder.Notes.Add);

        Assert.Equal(MenuChoiceResult.GreyedOut, result);
        Assert.Equal(0, recorder.Presses);
        Assert.Equal("only", string.Join(",", recorder.Tried));
    }

    [Fact]
    public void EveryStrategyGetsItsChanceBeforeTheRunCallsTheCommandAbsent()
    {
        var recorder = new Recorder();
        var attempts = new[]
        {
            ("one", recorder.Attempt("one")),
            ("two", recorder.Attempt("two")),
            ("three", recorder.Attempt("three")),
        };

        var result = CreateMenuChooser.Choose(
            attempts,
            () => MenuPopupState.NotPoppedUp,
            recorder.Press,
            recorder.Notes.Add);

        Assert.Equal(MenuChoiceResult.NeverOpened, result);
        Assert.Equal("one,two,three", string.Join(",", recorder.Tried));
        Assert.Equal(0, recorder.Presses);
    }

    [Fact]
    public void AnOpenMenuWhoseEntryWillNotPressIsReportedSeparatelyFromAnAbsentOne()
    {
        var recorder = new Recorder();
        var attempts = new[] { ("only", recorder.Attempt("only")) };

        var result = CreateMenuChooser.Choose(
            attempts,
            () => MenuPopupState.Pressable,
            recorder.FailToPress,
            recorder.Notes.Add);

        Assert.Equal(MenuChoiceResult.PressFailed, result);
        Assert.Equal(1, recorder.Presses);
    }

    /// <summary>
    /// What each read-back costs the run, said in the log rather than inferred from a timeout.
    ///
    /// The four states exist because they are four different things a user can do something about,
    /// and the chooser's whole value is that it stops treating them as one: "the menu never opened"
    /// is a retry, "Live greyed the entry out" is a refusal to wait through. A test that only asked
    /// the outcome would pass with three of the notes swapped, which is the exact way this message
    /// surface went stale once already.
    /// </summary>
    [Theory]
    [InlineData(MenuPopupState.NotPoppedUp, MenuChoiceResult.NeverOpened, "did not bring up the Create menu")]
    [InlineData(MenuPopupState.ClosedAgain, MenuChoiceResult.NeverOpened, "opened the Create menu and it closed again")]
    [InlineData(MenuPopupState.GreyedOut, MenuChoiceResult.GreyedOut, "shows Separate Stems greyed out after 'only'")]
    [InlineData(MenuPopupState.Pressable, MenuChoiceResult.Chosen, null)]
    public void EachReadBackOfThePopupIsReportedAsWhatItMeans(
        MenuPopupState observed, MenuChoiceResult expected, string? expectedNote)
    {
        var recorder = new Recorder();
        var attempts = new[] { ("only", recorder.Attempt("only")) };

        var result = CreateMenuChooser.Choose(
            attempts, () => observed, recorder.Press, recorder.Notes.Add);

        Assert.Equal(expected, result);
        Assert.Equal("only", string.Join(",", recorder.Tried));

        if (expectedNote is null)
            Assert.Empty(recorder.Notes);
        else
            Assert.Contains(expectedNote, recorder.Notes.Single());
    }

    /// <summary>
    /// A greyed-out entry ends the sequence. The strategies after it would each open the menu again
    /// to press the same disabled item, and Live's refusal is not a miss: the run spent a dialog
    /// timeout per attempt on exactly that loop before this clause existed.
    /// </summary>
    [Fact]
    public void ARefusedCommandIsNotRetriedByTheStrategiesThatFollow()
    {
        var recorder = new Recorder();
        var attempts = new[]
        {
            ("first", recorder.Attempt("first")),
            ("second", recorder.Attempt("second")),
        };

        var pressed = false;
        var result = CreateMenuChooser.Choose(
            attempts,
            () => MenuPopupState.GreyedOut,
            () => { pressed = true; return true; },
            recorder.Notes.Add);

        Assert.Equal(MenuChoiceResult.GreyedOut, result);
        Assert.Equal("first", string.Join(",", recorder.Tried));
        Assert.False(pressed);
    }

    // Read from Live's own UI Automation tree, which is the only coordinate system a click can
    // be aimed in: a maximised Live 12.3.2 on a 1920x1080 primary monitor reports GetWindowRect
    // (-8,-8,1936,1066) and the "Create" entry as MenuItem rect=(67,23,48,23), centre (91,34).
    // Numbers taken off a screenshot are not these - a 2560-wide rendering of this same capture
    // puts the row at y=39..62 and the entry's centre at (92,50), which is 1.333x too low and
    // aims a click at Live's transport row.
    private const int LiveWindowLeft = -8;
    private const int LiveWindowTop = -8;
    private const int LiveWindowWidth = 1936;
    private const int LiveWindowHeight = 1066;

    private static readonly WindowRect LiveWindow = new(
        LiveWindowLeft, LiveWindowTop, LiveWindowWidth, LiveWindowHeight);

    private const int CreateMenuLeft = 67;
    private const int CreateMenuTop = 23;
    private const int CreateMenuWidth = 48;
    private const int CreateMenuHeight = 23;

    [Fact]
    public void TheDefaultProfileAimsTheCoordinateFallbackAtTheMenuRowItWasMeasuredOn()
    {
        var profile = new LiveUiProfile();
        var (x, y) = LiveWindow.FractionPoint(profile.CreateMenuXFraction, profile.MenuBarYFraction);

        Assert.InRange(x, CreateMenuLeft, CreateMenuLeft + CreateMenuWidth);
        Assert.InRange(y, CreateMenuTop, CreateMenuTop + CreateMenuHeight);
    }

    [Fact]
    public void AMenuRowFractionTakenFromAScaledScreenshotAimsBelowTheMenuBar()
    {
        // 0.055 was derived from a screenshot rendered 1.333x larger than the screen it measured,
        // and it is what this test exists to keep out: it lands in the transport row underneath.
        var (x, y) = LiveWindow.FractionPoint(0.052, 0.055);

        Assert.InRange(x, CreateMenuLeft, CreateMenuLeft + CreateMenuWidth);
        Assert.True(y > CreateMenuTop + CreateMenuHeight, $"y={y} should sit below the menu row");
    }

    [Fact]
    public void APointIsRelativeToTheRectanglesOwnOriginSoMovingTheWindowMovesTheClick()
    {
        var moved = new WindowRect(500, 300, LiveWindowWidth, LiveWindowHeight).FractionPoint(0.052, 0.039);
        var still = LiveWindow.FractionPoint(0.052, 0.039);

        Assert.Equal(500 - LiveWindowLeft + still.Left, moved.Left);
        Assert.Equal(300 - LiveWindowTop + still.Top, moved.Top);
    }

    [Fact]
    public void NoStrategiesAtAllIsAnAbsentCommandRatherThanAHang()
    {
        var recorder = new Recorder();

        Assert.Equal(
            MenuChoiceResult.NeverOpened,
            CreateMenuChooser.Choose([], () => MenuPopupState.Pressable, recorder.Press, recorder.Notes.Add));

        Assert.Empty(recorder.Notes);
    }
}

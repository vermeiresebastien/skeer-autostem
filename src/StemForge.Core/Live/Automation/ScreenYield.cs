namespace StemForge.Core.Live.Automation;

/// <summary>
/// What the run may do with the screen while Ableton Live renders.
///
/// Live only accepts its menu and clip commands as the foreground window - it ignores posted
/// messages, so those clicks have to be real ones - but it needs no input at all while it
/// renders, and rendering is the part that takes minutes. This is where the screen goes back
/// to the user for that stretch, and where the run keeps its hands off a window the user has
/// already taken.
/// </summary>
public enum ScreenYieldPlan
{
    /// <summary>No window was captured, or it has since closed. Leave the screen alone.</summary>
    NothingToHandBack,

    /// <summary>
    /// The window is minimised. Activating it would restore something the user chose to put away.
    /// </summary>
    OwnerHasGoneAway,

    /// <summary>Live itself had the screen before the run started, so raising it changes nothing.</summary>
    LiveAlreadyOwnsTheScreen,

    /// <summary>The window the user was looking at is still the foreground one.</summary>
    AlreadyHandedBack,

    /// <summary>
    /// Some other window is in front and the run does not own it. The user has taken the screen
    /// back themselves; fighting them for it is not what this feature is for.
    /// </summary>
    UserHasTheScreen,

    /// <summary>Put the captured window in front for the duration of the render.</summary>
    HandBack,
}

/// <summary>The window facts <see cref="ScreenYield"/> decides from, gathered by the caller.</summary>
public readonly record struct ScreenYieldFacts(
    bool OwnerExists,
    bool OwnerIsVisible,
    bool OwnerIsLive,
    bool OwnerIsForeground,
    bool ForegroundIsOursOrLive);

public static class ScreenYield
{
    public static ScreenYieldPlan Plan(in ScreenYieldFacts facts)
    {
        if (!facts.OwnerExists) return ScreenYieldPlan.NothingToHandBack;
        if (!facts.OwnerIsVisible) return ScreenYieldPlan.OwnerHasGoneAway;
        if (facts.OwnerIsLive) return ScreenYieldPlan.LiveAlreadyOwnsTheScreen;
        if (facts.OwnerIsForeground) return ScreenYieldPlan.AlreadyHandedBack;
        if (!facts.ForegroundIsOursOrLive) return ScreenYieldPlan.UserHasTheScreen;

        return ScreenYieldPlan.HandBack;
    }
}

using StemForge.Core.Models;
using static StemForge.Core.Live.Automation.NativeMethods;

namespace StemForge.Core.Live.Automation;

/// <summary>Did the input go out or not. There is no third answer.</summary>
public enum InjectionOutcome
{
    /// <summary>Every guard held and <c>SendInput</c> was called.</summary>
    Injected,

    /// <summary>A guard refused, so nothing was sent and <see cref="InjectionResult.Reason"/> says why.</summary>
    Refused,
}

/// <summary>
/// What one injection attempt decided, and the sentence that explains it.
///
/// <see cref="Reason"/> is a kebab-code rather than prose so a log can be searched for it:
/// the same refusal repeated across a batch is one grep, and a run that refused often is one
/// count. <see cref="Detail"/> carries the window names that make a refusal actionable.
/// </summary>
public readonly record struct InjectionResult(InjectionOutcome Outcome, string Reason, string Detail)
{
    public bool Injected => Outcome == InjectionOutcome.Injected;

    /// <summary>The failure the user has to act on, as the runner's error kinds name it.</summary>
    public ErrorKind Kind => Reason switch
    {
        GuardedInput.DesktopNotInteractive => ErrorKind.DesktopLocked,
        _ => ErrorKind.LiveLostFocus,
    };

    /// <summary>The one-line record of an attempt, in the same shape wherever input is sent or refused.</summary>
    public string Sentence => Injected ? $"injected: {Detail}" : $"refused: {Reason} - {Detail}";
}

/// <summary>
/// The only door to synthetic input in this product.
///
/// Everything above it - Live's menu bar, a clip slot, one of Live's dialogs - is clicked or
/// keyed by asking <em>this</em> class, and every one of its methods re-reads the desktop and the
/// foreground at the moment of the call rather than trusting what a caller verified earlier. That
/// is the whole reason it exists. The stages that use it focus Live and then wait, and those waits
/// run to tens of seconds: a click aimed by what the focus call saw thirty seconds ago is a click
/// aimed at whatever the user opened in the meantime. The rule this file's name is the enforcement
/// of is the one the runner has always stated - input is only sent after the target window, the
/// foreground state and the desktop are verified - and the verification is now not separable from
/// the sending.
///
/// Two things follow from being the only door:
///   * <see cref="InputSimulator"/>'s injection methods are internal. A refusal here, not a
///     compile error somewhere else, is the only way input can be declined.
///   * Refusing is a normal outcome. Nothing here throws to say "I am not doing that"; the caller
///     gets a reason code and decides whether the job continues without that click.
///
/// Where Live will take a message instead of a gesture, no caller should come here at all:
/// <see cref="InputSimulator.PostKey"/> addresses one window's own queue, needs neither the
/// foreground nor a desktop, and cannot land in the user's window. See the product rule that puts
/// the key first and the click second.
/// </summary>
public static class GuardedInput
{
    // Refusal reasons. Greppable, one per decision, never reused for a different decision.
    public const string DesktopNotInteractive = "desktop-not-interactive";
    public const string TargetWindowGone = "target-window-gone";
    public const string ForegroundIsNotTheTarget = "foreground-is-not-the-target";
    public const string CoveredAtTheTargetPoint = "covered-at-the-target-point";

    /// <summary>
    /// Clicks <paramref name="at"/> (screen pixels) on behalf of <paramref name="targetWindow"/>.
    /// Refuses unless the point really belongs to that window: a click goes to whatever is topmost
    /// under the pointer, and Live's own rectangle says nothing about what is in front of it.
    /// </summary>
    public static InjectionResult Click(IntPtr targetWindow, System.Drawing.Point at, string purpose)
    {
        if (RefusalReason(targetWindow, at) is { } reason) return Refused(reason, targetWindow, at);

        InputSimulator.LeftClick(at.X, at.Y);
        return new InjectionResult(InjectionOutcome.Injected, "injected", $"{purpose} at {at.X},{at.Y}");
    }

    /// <summary>
    /// Presses one key into <paramref name="targetWindow"/>. A keystroke has no coordinate to be
    /// aimed by, so the only thing that can be checked is the stronger demand: the window being
    /// keyed has to be the window that is actually in front.
    /// </summary>
    public static InjectionResult PressKey(IntPtr targetWindow, ushort virtualKey, string purpose)
    {
        if (RefusalReason(targetWindow, null) is { } reason) return Refused(reason, targetWindow, null);

        InputSimulator.KeyPress(virtualKey);
        return new InjectionResult(InjectionOutcome.Injected, "injected", $"{purpose} (key 0x{virtualKey:X2})");
    }

    /// <summary>A modifier and a key, aimed at one window, under the same rule as <see cref="PressKey"/>.</summary>
    public static InjectionResult PressChord(IntPtr targetWindow, ushort modifier, ushort virtualKey, string purpose)
    {
        if (RefusalReason(targetWindow, null) is { } reason) return Refused(reason, targetWindow, null);

        InputSimulator.Chord(modifier, virtualKey);
        return new InjectionResult(InjectionOutcome.Injected, "injected",
            $"{purpose} (keys 0x{modifier:X2}+0x{virtualKey:X2})");
    }

    /// <summary>
    /// Types into one window, under the same rule as <see cref="PressKey"/>. Text is the input that
    /// does the most damage in the wrong place, so it goes nowhere unless the target is frontmost.
    /// </summary>
    public static InjectionResult TypeText(IntPtr targetWindow, string text, string purpose)
    {
        if (RefusalReason(targetWindow, null) is { } reason) return Refused(reason, targetWindow, null);

        InputSimulator.TypeText(text);
        return new InjectionResult(InjectionOutcome.Injected, "injected",
            $"{purpose} ({text.Length} character(s))");
    }

    /// <summary>
    /// The synthetic ALT tap that makes this process eligible for
    /// <see cref="WindowControl.Activate"/>'s next <c>SetForegroundWindow</c>.
    ///
    /// This is the one injection in the product that cannot require the target to be in front:
    /// taking the foreground is the thing it is for, and insisting on it beforehand asks for a
    /// window that is already there. What it does require is an interactive desktop, because an
    /// ALT tap sent while Windows owns the screen goes to the lock screen. It presses no key that
    /// any application acts on by itself.
    /// </summary>
    public static InjectionResult TapAltForActivation(string purpose)
    {
        if (WindowControl.QueryDesktop() != WindowControl.DesktopState.Interactive)
            return Refused(DesktopNotInteractive, IntPtr.Zero, null);

        InputSimulator.KeyPress(InputSimulator.VkLeftAlt);
        return new InjectionResult(InjectionOutcome.Injected, "injected", purpose);
    }

    /// <summary>
    /// The verdict, and nothing else: no input is sent by asking this. The click path passes the
    /// point it means to press; the key path passes null, because a keystroke is aimed at a window
    /// rather than a coordinate.
    /// </summary>
    /// <returns>The reason injection is not allowed, or null when every guard holds.</returns>
    public static string? RefusalReason(IntPtr targetWindow, System.Drawing.Point? at = null)
    {
        if (WindowControl.QueryDesktop() != WindowControl.DesktopState.Interactive)
            return DesktopNotInteractive;

        if (targetWindow == IntPtr.Zero || !IsWindow(targetWindow)) return TargetWindowGone;

        var targetRoot = RootOf(targetWindow);
        if (targetRoot == IntPtr.Zero) return TargetWindowGone;

        var foregroundRoot = RootOf(GetForegroundWindow());
        GetWindowThreadProcessId(targetRoot, out var targetPid);
        GetWindowThreadProcessId(foregroundRoot, out var foregroundPid);

        if (at is null)
        {
            // A keystroke goes to the focused window of the foreground thread, so "is this window
            // the front one" is the only question that means anything - and it is asked of the
            // top-level window, not the process: Live's main window being in front says nothing
            // about whether the dialog being keyed is.
            return foregroundRoot == targetRoot ? null : ForegroundIsNotTheTarget;
        }

        // A click goes to whatever is topmost at its point, which is a different question from who
        // is in front and the one that actually decides where the press lands. Both are asked: the
        // hit test catches Live's own window standing in for a popup that has closed, and the
        // process check keeps a click out of a desktop owned by somebody else's application.
        var hitRoot = RootOf(WindowFromPoint(new POINT { X = at.Value.X, Y = at.Value.Y }));
        if (hitRoot != targetRoot) return CoveredAtTheTargetPoint;

        if (foregroundRoot != targetRoot && foregroundPid != targetPid) return ForegroundIsNotTheTarget;

        return null;
    }

    /// <summary>The top-level window a handle belongs to, or <see cref="IntPtr.Zero"/>.</summary>
    public static IntPtr RootOf(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return IntPtr.Zero;

        try
        {
            var root = GetAncestor(hWnd, GaRoot);
            return root == IntPtr.Zero ? hWnd : root;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static InjectionResult Refused(string reason, IntPtr targetWindow, System.Drawing.Point? at)
    {
        var foreground = WindowControl.GetForegroundWindow();

        return new InjectionResult(InjectionOutcome.Refused, reason, at is null
            ? $"'{WindowControl.GetTitle(targetWindow)}' is not the window in front; " +
              $"'{WindowControl.GetTitle(foreground)}' is"
            : $"the point {at.Value.X},{at.Value.Y} belongs to " +
              $"'{WindowControl.GetTitle(RootOf(WindowFromPoint(new POINT { X = at.Value.X, Y = at.Value.Y })))}' " +
              $"and the foreground is '{WindowControl.GetTitle(foreground)}', " +
              $"not '{WindowControl.GetTitle(targetWindow)}'");
    }
}

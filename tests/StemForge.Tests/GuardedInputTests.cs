using System.Windows.Forms;
using StemForge.Core.Live.Automation;
using StemForge.Core.Models;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The one door synthetic input goes through, tested from the outside - which means from the side
/// where it stays shut.
///
/// <see cref="GuardedInput"/> exists because a click or a keystroke is aimed at a moment, not at a
/// plan: Live is focused, then the run waits ten or twenty seconds for a menu, and what is in front
/// when the input finally goes out is whatever the user brought up in that time. So the guard
/// re-reads the desktop and the foreground at the instant of the send, and these tests are that
/// instant - with windows this file makes, off every monitor, none of them ever focused, and no
/// premise about what else happens to be parked out there.
///
/// What this file cannot prove is the half that needs a target to BE the foreground window: that an
/// allowed click or key really goes out. Making one of these fake windows the foreground would take
/// the screen away from whoever is using this machine, which is the thing the product exists to
/// avoid. Measured, when a window of this file was raised into the top-most band to settle a Z-order
/// question, it became the foreground window and the guard correctly answered "send it" - so the
/// stacking here is searched for, never forced.
/// <see cref="TheWindowInTheFrontIsNotRefusedForLackingTheForeground"/> covers as much of the
/// allowed half as can be covered honestly - the verdict, asked of the window that genuinely is in
/// front, with nothing sent - and the injection itself is owed a real Live run.
///
/// Nothing here asserts on the cursor or on the foreground as a measurement of success: the user's
/// foreground moves for reasons of its own, and a test that reads it once will sometimes read a
/// different desktop than the one it is judging. Every test asserts that
/// <see cref="InputSimulator.InjectionAttempts"/> did not change, which is a count inside this
/// process and cannot be disturbed from outside it.
/// </summary>
public sealed class GuardedInputTests
{
    /// <summary>
    /// Far outside every monitor, so no window the user is looking at can be disturbed by what this
    /// class does to these rectangles.
    ///
    /// Empty is a different matter, and was assumed until it bit: a desktop parks whole windows at
    /// negative coordinates - measured here on one machine, first a browser tab, then a background
    /// process's invisible top-level window, both owning the exact point these tests aim at. So the
    /// Z-order is never taken on trust: <see cref="Contested"/> looks for a point where one of the
    /// two windows this class made really does win the hit test, and moves the pair somewhere else
    /// until it finds one.
    ///
    /// Which is also why none of this raises a window into the top-most band to settle the question.
    /// Measured: a window created with WinForms' <c>TopMost</c> becomes the foreground window even
    /// with <see cref="QuietWindow.ShowWithoutActivation"/> - it takes the screen from the person
    /// using the machine, the exact thing the product exists to avoid, and it made every verdict in
    /// this class say "allowed".
    /// </summary>
    private const int OffscreenX = -32000;
    private const int OffscreenY = -32000;

    /// <summary>Inside both fake windows' rectangles - the point they are made to contest.</summary>
    private static readonly System.Drawing.Point Aim = new(OffscreenX + 20, OffscreenY + 20);

    /// <summary>Inside neither: where a click aimed by a stale rectangle used to land.</summary>
    private static readonly System.Drawing.Point Elsewhere = new(OffscreenX + 900, OffscreenY + 900);

    /// <summary>
    /// Corners of the off-screen region to try the contested pair in, far enough apart that one
    /// parked window cannot cover them all.
    /// </summary>
    private static readonly (int X, int Y)[] Spots =
    [
        (0, 0), (2000, 0), (0, 2000), (4000, 2000), (2000, 4000), (6000, 6000),
    ];

    /// <summary>An off-screen window with a caption, shown but never activated.</summary>
    private sealed class QuietWindow : Form
    {
        internal QuietWindow(string title, int left, int top)
        {
            Text = title;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(left, top);
            ClientSize = new System.Drawing.Size(120, 60);
            FormBorderStyle = FormBorderStyle.FixedSingle;
        }

        /// <summary>Windows must not answer a Show by taking the screen from the person using it.</summary>
        protected override bool ShowWithoutActivation => true;
    }

    private static FakeFormHost Show(string title, int left = OffscreenX, int top = OffscreenY) =>
        new(() => new QuietWindow(title, left, top));

    /// <summary>Whether the desktop will say what is in front. A locked screen answers nothing.</summary>
    private static bool DesktopAnswers =>
        WindowControl.QueryDesktop() == WindowControl.DesktopState.Interactive;

    /// <summary>
    /// A refusal is certain; which reason is recorded depends on whether the desktop can be read at
    /// all. A locked screen is a legitimate state for the machine running a suite, and it is itself a
    /// refusal - so the outcome is asserted always and the reason while the desktop answers.
    /// </summary>
    private static void AssertRefused(string expectedReason, InjectionResult result, int injectionsBefore)
    {
        Assert.False(result.Injected);

        if (DesktopAnswers)
            Assert.Equal(expectedReason, result.Reason);
        else
            Assert.Equal(GuardedInput.DesktopNotInteractive, result.Reason);

        // The line a run logs: reason code first, then the window names that make it actionable.
        Assert.StartsWith("refused: ", result.Sentence, StringComparison.Ordinal);
        Assert.Equal(injectionsBefore, InputSimulator.InjectionAttempts);
    }

    /// <summary>
    /// Asks the pure question, and only then walks into the entry point that is able to send.
    ///
    /// This is not redundancy for its own sake: this machine's foreground can change while a test is
    /// running, and a suite is not allowed to discover the accept side by moving the user's pointer.
    /// A refusal is what every case below expects, so the verdict is confirmed first and the send is
    /// reached only behind a door that is already shut - if the answer ever comes back "allowed",
    /// this assert is where the test stops instead of on the user's desktop.
    /// </summary>
    private static InjectionResult ClickThatMustBeRefused(
        IntPtr target, System.Drawing.Point at, string purpose)
    {
        Assert.NotNull(GuardedInput.RefusalReason(target, at));
        return GuardedInput.Click(target, at, purpose);
    }

    private static InjectionResult KeyThatMustBeRefused(IntPtr target, ushort virtualKey, string purpose)
    {
        Assert.NotNull(GuardedInput.RefusalReason(target));
        return GuardedInput.PressKey(target, virtualKey, purpose);
    }

    /// <summary>
    /// The same door for the two entry points that carry text, so that no send-capable call in this
    /// file stands without a shut gate in front of it. That is what makes a guard mutation safe to
    /// run: weakening the predicate turns these asserts red at the pre-check, and the desktop never
    /// sees the keystroke the weakened code would have let through.
    /// </summary>
    private static InjectionResult TextThatMustBeRefused(IntPtr target, string text, string purpose)
    {
        Assert.NotNull(GuardedInput.RefusalReason(target));
        return GuardedInput.TypeText(target, text, purpose);
    }

    private static InjectionResult ChordThatMustBeRefused(
        IntPtr target, ushort modifier, ushort virtualKey, string purpose)
    {
        Assert.NotNull(GuardedInput.RefusalReason(target));
        return GuardedInput.PressChord(target, modifier, virtualKey, purpose);
    }

    /// <summary>
    /// A point inside both windows that one of them genuinely wins, with the winner named, or null
    /// if the whole overlap belongs to somebody else.
    ///
    /// Ownership is asked of the guard itself rather than of a private hit-test, and the premise is
    /// symmetric because which of two windows Windows stacks on top is not something a test may
    /// decide: a target whose only complaint is the foreground owns the point, and the one refused
    /// as covered does not. Finding the point is what keeps the assertions below from comparing
    /// accidents.
    /// </summary>
    private static (IntPtr Owner, IntPtr Under, System.Drawing.Point At)? Contested(IntPtr a, IntPtr b)
    {
        var rectA = WindowControl.GetRect(a);
        var rectB = WindowControl.GetRect(b);
        if (rectA is null || rectB is null) return null;

        var left = Math.Max(rectA.X, rectB.X);
        var top = Math.Max(rectA.Y, rectB.Y);
        var right = Math.Min(rectA.X + rectA.Width, rectB.X + rectB.Width);
        var bottom = Math.Min(rectA.Y + rectA.Height, rectB.Y + rectB.Height);

        for (var y = top + 3; y < bottom - 2; y += 5)
        for (var x = left + 3; x < right - 2; x += 5)
        {
            var at = new System.Drawing.Point(x, y);
            var verdictA = GuardedInput.RefusalReason(a, at);
            var verdictB = GuardedInput.RefusalReason(b, at);

            // Exactly one of the two wins this pixel, and the loser's complaint is that the winner is
            // standing on it. Anything else - both covered by a third window, or an answer that is
            // not a refusal at all - is not a contested point.
            if (verdictA == GuardedInput.ForegroundIsNotTheTarget
                && verdictB == GuardedInput.CoveredAtTheTargetPoint) return (a, b, at);

            if (verdictB == GuardedInput.ForegroundIsNotTheTarget
                && verdictA == GuardedInput.CoveredAtTheTargetPoint) return (b, a, at);
        }

        return null;
    }

    [Fact]
    public void AWindowThatIsNotTheForegroundOneIsNeverKeyed()
    {
        var before = InputSimulator.InjectionAttempts;
        using var window = Show("StemForge guarded-input quiet");

        // This test never takes the foreground, which is what makes it an honest witness: the key is
        // refused against the desktop as the user actually left it.
        var escape = KeyThatMustBeRefused(window.Handle, InputSimulator.VkEscape, "escaped a Live prompt");

        AssertRefused(GuardedInput.ForegroundIsNotTheTarget, escape, before);
    }

    /// <summary>
    /// The guard has to be able to say yes, or every refusal here passes for the wrong reason - a
    /// helper that refused everything would be indistinguishable from a working one. Asked of the
    /// window that genuinely is in front, the verdict is "send it", and asking is not sending, which
    /// is what lets this be checked on a machine somebody is using instead of one sitting idle.
    /// </summary>
    [Fact]
    public void TheWindowInTheFrontIsNotRefusedForLackingTheForeground()
    {
        var before = InputSimulator.InjectionAttempts;

        // Read twice, because this measurement is of a desktop somebody is using and the foreground
        // it names can change in the microsecond between asking and judging. Sampling it once made
        // this red on a machine whose user switched windows mid-suite - a real event, and not a
        // defect in the guard. What is promised is that some reading of the front window answered
        // "send it"; the injections stay at zero whatever happens.
        var foreground = IntPtr.Zero;
        string? keyVerdict = null;
        string? clickVerdict = null;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            foreground = GuardedInput.RootOf(WindowControl.GetForegroundWindow());
            if (foreground == IntPtr.Zero) break;

            var rect = WindowControl.GetRect(foreground);
            if (rect is null || rect.Width < 64 || rect.Height < 64) continue;

            // A keystroke is aimed at a window, and this window is the one in front: that is the
            // whole condition, so the only honest answer is null.
            keyVerdict = GuardedInput.RefusalReason(foreground);
            if (keyVerdict is not null) continue;

            // A click is aimed at a point inside it. Something always-on-top may legitimately own
            // that pixel, so only the foreground clause is promised here - the two clauses
            // disagreeing is the business of the cases below.
            clickVerdict = GuardedInput.RefusalReason(
                foreground, new System.Drawing.Point(rect.CenterX, rect.CenterY));
            break;
        }

        // A desktop that answers nothing, and a moment with no window in front at all, are both
        // states this machine can legitimately be in. The guard's first clause covers the first and
        // the tests above pin what it says there.
        if (foreground == IntPtr.Zero || !DesktopAnswers)
        {
            Assert.Equal(before, InputSimulator.InjectionAttempts);
            return;
        }

        Assert.Null(keyVerdict);
        Assert.NotEqual(GuardedInput.ForegroundIsNotTheTarget, clickVerdict ?? string.Empty);
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    [Fact]
    public void AHandleWhoseWindowHasGoneIsRefusedRatherThanGuessedAt()
    {
        var before = InputSimulator.InjectionAttempts;
        IntPtr handle;

        using (var window = Show("StemForge guarded-input vanishing"))
        {
            handle = window.Handle;
        }

        // A Live that closes between the read and the send used to be a keystroke into whatever new
        // window reused that address.
        AssertRefused(GuardedInput.TargetWindowGone,
            KeyThatMustBeRefused(handle, InputSimulator.VkReturn, "confirmed a Live dialog"), before);

        Assert.False(GuardedInput.PressKey(IntPtr.Zero, InputSimulator.VkReturn, "nothing").Injected);
        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    /// <summary>
    /// The window comparison, which a process comparison cannot make.
    ///
    /// Two windows of this one process - both made here, so their process id is this one and is
    /// identical for the pair - overlapping at a point that one of them is first shown to win. A
    /// process id, all the check that used to be here compared, cannot produce two answers for two
    /// windows of one process, and that is exactly how PressSeparateStems came to press Live's
    /// session view through Live's own menu popup: the popup had closed, its rectangle still named
    /// the spot, and "same application" was read as "safe". Live's popups and Live's session view are
    /// this shape - one process, two top-level windows.
    ///
    /// The answers demanded are the two halves of that story: the window that owns the point is
    /// refused only for not being in front, and the window the point was read out of is refused
    /// because something else is standing over it. Neither is refused for having gone.
    /// </summary>
    [Fact]
    public void TwoWindowsOfOneProcessAreJudgedByTheirOwnRectangles()
    {
        var before = InputSimulator.InjectionAttempts;

        // A locked or switching desktop answers nothing at all, and that is itself the guard's first
        // clause - which is pinned here, because no window can be asked to win a pixel until the
        // desktop is reading out again.
        if (!DesktopAnswers)
        {
            using var window = Show("StemForge guarded-input locked");

            Assert.Equal(GuardedInput.DesktopNotInteractive,
                GuardedInput.RefusalReason(GuardedInput.RootOf(window.Handle), Aim));
            Assert.Equal(before, InputSimulator.InjectionAttempts);
            return;
        }

        FakeFormHost? first = null;
        FakeFormHost? second = null;
        (IntPtr Owner, IntPtr Under, System.Drawing.Point At)? contested = null;

        try
        {
            foreach (var spot in Spots)
            {
                first?.Dispose();
                second?.Dispose();

                first = Show("StemForge guarded-input popup", OffscreenX + spot.X, OffscreenY + spot.Y);
                second = Show("StemForge guarded-input main", OffscreenX + spot.X - 5, OffscreenY + spot.Y - 5);

                contested = Contested(GuardedInput.RootOf(first.Handle), GuardedInput.RootOf(second.Handle));
                if (contested is not null) break;
            }

            Assert.True(contested is not null,
                "no point inside either fake window was won by exactly one of them, in any of the "
                + $"{Spots.Length} corners of the off-screen region tried. Two readings of that: this "
                + "desktop parks whole windows over every spot there is, or the guard has stopped "
                + "telling two windows of one process apart and answers both the same everywhere.");

            var (owner, under, at) = contested!.Value;

            // The point really is inside both rectangles, which is what makes it the case the review
            // named: one process, two windows, both of them candidates for the click.
            foreach (var root in new[] { owner, under })
            {
                var rect = WindowControl.GetRect(root);
                Assert.NotNull(rect);
                Assert.InRange(at.X, rect.X, rect.X + rect.Width);
                Assert.InRange(at.Y, rect.Y, rect.Y + rect.Height);
            }

            var ownerVerdict = GuardedInput.RefusalReason(owner, at);
            var underVerdict = GuardedInput.RefusalReason(under, at);

            Assert.Equal(GuardedInput.ForegroundIsNotTheTarget, ownerVerdict);
            Assert.Equal(GuardedInput.CoveredAtTheTargetPoint, underVerdict);
            Assert.NotEqual(ownerVerdict, underVerdict);

            // Neither is refused for being gone, and the one that wins the pixel is refused for what
            // the pid check used to wave through: the foreground is somebody else's.
            Assert.DoesNotContain(GuardedInput.TargetWindowGone, new[] { ownerVerdict, underVerdict });

            // And the window standing under the other one is refused for that reason through the entry
            // point a click actually takes - which is where the verdict becomes a message.
            AssertRefused(
                GuardedInput.CoveredAtTheTargetPoint,
                ClickThatMustBeRefused(under, at, "clicked Live's Create menu"),
                before);
        }
        finally
        {
            second?.Dispose();
            first?.Dispose();
        }
    }

    /// <summary>
    /// A click and a keystroke are different questions and must not be collapsed into one check. A
    /// keystroke has no coordinate to be aimed by, so the only thing that can be asked of it is the
    /// stronger question: the window being keyed has to be the window in front. A click is aimed
    /// somewhere, and a point the target does not own refuses it wherever the foreground happens to
    /// be. Proving the two verdicts are reached separately is what keeps a later "simplify this" from
    /// weakening one of them.
    /// </summary>
    [Fact]
    public void AKeyAndAPointAreJudgedByTheirOwnRules()
    {
        var before = InputSimulator.InjectionAttempts;
        using var window = Show("StemForge guarded-input split");

        var key = GuardedInput.RefusalReason(window.Handle);
        var click = GuardedInput.RefusalReason(window.Handle, Elsewhere);

        Assert.NotNull(key);
        Assert.NotNull(click);

        if (DesktopAnswers)
        {
            Assert.Equal(GuardedInput.ForegroundIsNotTheTarget, key);

            // The stale-rectangle case: the element's bounding box was read before its window moved
            // or closed, and the point it names belongs to nobody who was asked.
            Assert.Equal(GuardedInput.CoveredAtTheTargetPoint, click);
            Assert.NotEqual(key, click);
        }

        Assert.Equal(before, InputSimulator.InjectionAttempts);
    }

    [Fact]
    public void ARefusalIsAnAnswerAndNotAFault()
    {
        var before = InputSimulator.InjectionAttempts;
        using var window = Show("StemForge guarded-input calm");

        // Nothing about a refusal may throw: the stages that ask for input have to carry on and
        // report what they could not do, which is what a batch running unattended depends on.
        var click = ClickThatMustBeRefused(window.Handle, Aim, "clicked the clip slot");
        var key = KeyThatMustBeRefused(window.Handle, InputSimulator.VkEscape, "escaped a Live menu");
        var text = TextThatMustBeRefused(window.Handle, "C:\\x.als", "typed a path");
        var chord = ChordThatMustBeRefused(window.Handle, InputSimulator.VkControl, InputSimulator.VkS,
            "asked Live to save");

        Assert.False(click.Injected);
        Assert.False(key.Injected);
        Assert.False(text.Injected);
        Assert.False(chord.Injected);
        Assert.Equal(before, InputSimulator.InjectionAttempts);

        // A locked desktop stays a locked-desktop failure and a stolen foreground stays a lost-focus
        // one, because those are two different sentences for two different things the user can do.
        Assert.True(click.Kind is ErrorKind.DesktopLocked or ErrorKind.LiveLostFocus);
        Assert.Equal(click.Kind == ErrorKind.DesktopLocked, key.Kind == ErrorKind.DesktopLocked);
    }
}

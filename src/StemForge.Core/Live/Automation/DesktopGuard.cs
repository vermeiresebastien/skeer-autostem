using System.Threading;

namespace StemForge.Core.Live.Automation;

/// <summary>
/// The one question that has to be answered before input is injected: is the ordinary desktop
/// the one receiving input?
///
/// While Windows shows its lock or consent screen, synthetic input goes there instead of to the
/// desktop Live is sitting on - a keystroke lands in the password box. A desktop that cannot be
/// read at all is refused for the same reason as a locked one, but the two are reported
/// differently: only one of them is fixed by unlocking the screen, and the other is fixed by
/// running the app where a person can see it.
/// </summary>
public static class DesktopGuard
{
    /// <summary>How many reads a desktop that will not answer is retried before it is refused.</summary>
    public const int Attempts = 3;

    /// <summary>
    /// Throws unless the interactive desktop is up. <paramref name="whileRunning"/> says what the
    /// run was in the middle of, so the message tells the user where it stopped.
    /// </summary>
    public static void Require(string whileRunning)
    {
        var state = WindowControl.DesktopState.Undetermined;

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            state = WindowControl.QueryDesktop();
            if (state != WindowControl.DesktopState.Undetermined) break;
            Thread.Sleep(300);
        }

        if (state == WindowControl.DesktopState.Interactive) return;

        throw new StemForge.Core.Live.LiveAutomationException(
            StemForge.Core.Models.ErrorKind.DesktopLocked, state switch
            {
                WindowControl.DesktopState.Locked =>
                    $"Windows is locked{whileRunning}. Unlock the desktop, then resume the batch.",
                _ =>
                    $"Skeer autoSTEM could not read the Windows desktop{whileRunning}, so it stopped " +
                    "instead of sending input it cannot verify. This happens when the app runs in a " +
                    "session with no interactive desktop of its own - start it from the desktop you " +
                    "can see, then resume the batch.",
            });
    }
}

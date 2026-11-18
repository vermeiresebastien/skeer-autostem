using System.Diagnostics;
using System.Text;

namespace StemForge.Core.Live.Automation;

/// <summary>
/// State checks and window manipulation for the Live main window. Everything here is
/// about *verifying* the environment before any synthetic input is sent, which is what
/// lets the runner stop safely instead of typing into the wrong window.
/// </summary>
public static class WindowControl
{
    /// <summary>
    /// What the interactive desktop is doing. Three states, not a boolean: "we could not tell"
    /// and "it is locked" both stop automation, but they are different sentences to show a user,
    /// and only one of them is fixed by unlocking the screen.
    /// </summary>
    public enum DesktopState
    {
        /// <summary>The normal desktop is up and input will land where Live is.</summary>
        Interactive,

        /// <summary>The lock screen or the screensaver desktop owns the input.</summary>
        Locked,

        /// <summary>The input desktop could not be read at all.</summary>
        Undetermined,
    }

    /// <summary>
    /// Asks Windows which desktop is receiving input and reads its name. Live's window lives on
    /// "Default"; "Screen-saver" and "Winlogon" are the secure ones, where a click would be
    /// delivered to the lock screen rather than to Live.
    /// </summary>
    public static DesktopState QueryDesktop()
    {
        var desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DesiredRightsReadObjects);
        if (desktop == IntPtr.Zero) return DesktopState.Undetermined;

        try
        {
            var name = new StringBuilder(256);
            if (!NativeMethods.GetUserObjectInformationW(
                    desktop, NativeMethods.UoiName, name, (uint)(name.Capacity * 2), out _))
                return DesktopState.Undetermined;

            return name.ToString() switch
            {
                "Default" => DesktopState.Interactive,
                "Screen-saver" or "Winlogon" => DesktopState.Locked,
                _ => DesktopState.Undetermined,
            };
        }
        finally
        {
            NativeMethods.CloseDesktop(desktop);
        }
    }

    /// <summary>True when input must not be sent, whether because the desktop is locked or because it cannot be checked.</summary>
    public static bool IsDesktopLocked() => QueryDesktop() != DesktopState.Interactive;

    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public static bool IsForeground(IntPtr hWnd) => NativeMethods.GetForegroundWindow() == hWnd;

    public static WindowRect? GetRect(IntPtr hWnd)
    {
        if (!NativeMethods.IsWindow(hWnd)) return null;
        if (!NativeMethods.GetWindowRect(hWnd, out var rect)) return null;
        return new WindowRect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    public static string GetTitle(IntPtr hWnd) => NativeMethods.GetTitle(hWnd);

    /// <summary>True while the window exists and is on screen. A closed dialog stops being both.</summary>
    public static bool IsVisible(IntPtr hWnd) =>
        hWnd != IntPtr.Zero && NativeMethods.IsWindow(hWnd) && NativeMethods.IsWindowVisible(hWnd);

    /// <summary>
    /// Brings the window to the foreground.
    ///
    /// Windows deliberately blocks background processes from stealing the foreground, so
    /// this tries every documented escape hatch in order: a direct call, a synthetic ALT
    /// tap (which makes the calling thread eligible), sharing input state with the current
    /// foreground thread, and finally SwitchToThisWindow. A desktop app that is already the
    /// foreground process normally succeeds on the first attempt; a hidden console process
    /// may legitimately be unable to.
    /// </summary>
    public static bool Activate(IntPtr hWnd)
    {
        if (!NativeMethods.IsWindow(hWnd)) return false;
        if (IsForeground(hWnd)) return true;

        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SwRestore);
            Thread.Sleep(120);
        }

        NativeMethods.BringWindowToTop(hWnd);
        if (NativeMethods.SetForegroundWindow(hWnd) && IsForeground(hWnd)) return true;

        // The ALT tap is the standard way to become eligible for SetForegroundWindow. It goes
        // through the same door as every other keystroke, and it is the one call site that cannot
        // ask who is in front - taking the foreground is what it is for.
        try
        {
            GuardedInput.TapAltForActivation("the ALT tap that asks to become foreground-eligible");
            Thread.Sleep(60);
            if (NativeMethods.SetForegroundWindow(hWnd) && IsForeground(hWnd)) return true;
        }
        catch
        {
            // synthetic input can be rejected; keep trying other routes
        }

        // Share input state with the thread that currently owns the foreground.
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            var currentThread = NativeMethods.GetCurrentThreadId();
            var foregroundThread = foreground != IntPtr.Zero
                ? NativeMethods.GetWindowThreadProcessId(foreground, out _)
                : 0;
            var targetThread = NativeMethods.GetWindowThreadProcessId(hWnd, out _);

            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
                NativeMethods.AttachThreadInput(currentThread, targetThread, true);
                NativeMethods.BringWindowToTop(hWnd);
                NativeMethods.SetForegroundWindow(hWnd);
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);

                if (IsForeground(hWnd)) return true;
            }
        }
        catch
        {
            // best effort
        }

        try
        {
            NativeMethods.SwitchToThisWindow(hWnd, true);
            Thread.Sleep(150);
        }
        catch
        {
            // best effort
        }

        return IsForeground(hWnd);
    }

    /// <summary>
    /// What an activation attempt ended with, and the distinction the caller needs.
    ///
    /// <see cref="TargetIsForeground"/> means the exact window asked for is in front.
    /// <see cref="ProcessIsForeground"/> only means that *a* window of that process is - which is
    /// Live being Live (its own modal took the foreground) and is not a licence to click a covered
    /// window. A bool cannot carry both, and a caller that read the lenient one as the strict one
    /// pressed a button on a dialog the user could not see and spent one of its six answers on it.
    /// </summary>
    public readonly record struct ActivationOutcome(bool TargetIsForeground, bool ProcessIsForeground)
    {
        public bool Succeeded => TargetIsForeground || ProcessIsForeground;

        /// <summary>Why a window that did not come forward did not come forward.</summary>
        public string Describe(IntPtr hWnd) =>
            TargetIsForeground ? $"'{GetTitle(hWnd)}' is in front"
            : ProcessIsForeground
                ? $"the foreground is another window of the same application ('{GetTitle(GetForegroundWindow())}'), " +
                  $"not '{GetTitle(hWnd)}'"
                : $"the foreground belongs to '{GetTitle(GetForegroundWindow())}'";
    }

    /// <summary>
    /// Brings a window frontmost and believes the read-back rather than the refusal.
    ///
    /// SetForegroundWindow is declined - quietly, and with no reason to ask for - while the Start
    /// menu is open, while a toast is up and while the user is mid-click. All three let go on
    /// their own within a second or two, which is what a batch can afford to wait; one glance is
    /// what turned somebody opening his Start menu into LiveLostFocus and a killed Live.
    ///
    /// The read-back is asked at two levels, because they mean different things - see
    /// <see cref="ActivationOutcome"/>. Callers that send input go through
    /// <see cref="GuardedInput"/>, which re-asks the strict question at the moment of the send;
    /// this method only decides whether it is worth trying at all.
    /// </summary>
    public static ActivationOutcome TryActivateAndReport(IntPtr hWnd, int attempts = ForegroundAttempts)
    {
        NativeMethods.GetWindowThreadProcessId(hWnd, out var ownerPid);

        for (var attempt = 1; ; attempt++)
        {
            var isTarget = Activate(hWnd) || IsForeground(hWnd);
            if (isTarget) return new ActivationOutcome(true, true);

            // A modal of the same process holding the foreground is Live being itself, not a window
            // in the way - the click it wants will reach it, provided it is topmost where the click
            // lands. GuardedInput is what checks that half.
            if (ForegroundBelongsTo(ownerPid)) return new ActivationOutcome(false, true);

            if (attempt >= attempts) return new ActivationOutcome(false, false);
            Thread.Sleep(500);
        }
    }

    /// <summary>
    /// Asks for the window in front and returns true when that window or another of its
    /// application's took the foreground. A caller about to click a specific window wants
    /// <see cref="TryActivateAndReport"/> instead, and a caller about to key one wants
    /// <see cref="GuardedInput.PressKey"/>, which refuses unless the exact window is in front.
    /// </summary>
    public static bool TryActivate(IntPtr hWnd, int attempts = ForegroundAttempts) =>
        TryActivateAndReport(hWnd, attempts).Succeeded;

    /// <summary>True when the frontmost window belongs to that process.</summary>
    public static bool ForegroundBelongsTo(uint processId)
    {
        if (processId == 0) return false;
        var thread = NativeMethods.GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return thread != 0 && pid == processId;
    }

    /// <summary>How long a foreground that is not ours is waited out before it is called real.</summary>
    private const int ForegroundAttempts = 8;

    public static bool Maximize(IntPtr hWnd)
    {
        var ok = NativeMethods.ShowWindow(hWnd, NativeMethods.SwMaximize);
        Thread.Sleep(120);
        return ok;
    }

    /// <summary>Physical pixels per logical pixel for the window's monitor (96 = 100%).</summary>
    public static double GetScaleFactor(IntPtr hWnd)
    {
        try
        {
            var monitor = NativeMethods.MonitorFromWindow(hWnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor != IntPtr.Zero &&
                NativeMethods.GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpiX, out _) == 0 &&
                dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // Older systems without shcore fall through to 100%.
        }
        return 1.0;
    }

    /// <summary>Window title of a process's main window, or empty when there is none.</summary>
    public static string GetMainWindowTitle(Process process)
    {
        try { return process.MainWindowTitle ?? string.Empty; }
        catch { return string.Empty; }
    }
}

public sealed record WindowRect(int X, int Y, int Width, int Height)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;

    /// <summary>
    /// A point this fraction of the way across and down the rectangle, in screen pixels.
    ///
    /// The rectangle is the window's, so the caption and borders are part of the height being
    /// multiplied - which is what the profile's fractions were tuned against.
    /// </summary>
    public (int Left, int Top) FractionPoint(double xFraction, double yFraction) =>
        (X + (int)(Width * xFraction), Y + (int)(Height * yFraction));
}

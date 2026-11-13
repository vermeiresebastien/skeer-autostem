using System.Diagnostics;

namespace StemForge.Core.Live.Automation;

/// <summary>
/// State checks and window manipulation for the Live main window. Everything here is
/// about *verifying* the environment before any synthetic input is sent, which is what
/// lets the runner stop safely instead of typing into the wrong window.
/// </summary>
public static class WindowControl
{
    /// <summary>True when the interactive desktop is currently locked / on the secure desktop.</summary>
    public static bool IsDesktopLocked()
    {
        var desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DesiredRightsReadObjects);
        if (desktop == IntPtr.Zero) return true;
        NativeMethods.CloseDesktop(desktop);
        return false;
    }

    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public static bool IsForeground(IntPtr hWnd) => NativeMethods.GetForegroundWindow() == hWnd;

    public static WindowRect? GetRect(IntPtr hWnd)
    {
        if (!NativeMethods.IsWindow(hWnd)) return null;
        if (!NativeMethods.GetWindowRect(hWnd, out var rect)) return null;
        return new WindowRect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    public static string GetTitle(IntPtr hWnd) => NativeMethods.GetTitle(hWnd);

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

        // The ALT tap is the standard way to become eligible for SetForegroundWindow.
        try
        {
            InputSimulator.KeyPress(InputSimulator.VkLeftAlt);
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
}

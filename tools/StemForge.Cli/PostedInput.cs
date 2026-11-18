using System.Runtime.InteropServices;

namespace StemForge.Cli;

/// <summary>
/// Mouse and keyboard messages queued straight into one window's message queue. Nothing is
/// moved, raised or activated, so this is how the probe asks whether an application reads
/// the coordinates in the message or the position of the real cursor - the difference
/// between an engine that can run behind another window and one that needs the screen.
///
/// What every method here reports is <em>delivery</em>, and the two words that used to be
/// confused are "delivered" and "answered": <c>SendMessage</c> returns whatever the window
/// procedure returned, which for a mouse click is normally 0, so reading its result as a
/// delivery report printed <c>delivered=False</c> over a click that had just worked. Send mode
/// now uses <c>SendMessageTimeout</c>, whose own return value is exactly the question "did this
/// window receive and process the message before the timeout", and the window procedure's answer
/// is reported separately. A probe cited as evidence has to fail loudly or say nothing.
/// </summary>
internal static class PostedInput
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>Return from the window procedure and give up, rather than hanging the probe.</summary>
    private const uint SmtoAbortIfHung = 0x0002;

    private const uint TimeoutMilliseconds = 2000;

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;

    public static bool Click(IntPtr hWnd, int screenX, int screenY, bool useSend)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
        {
            Console.Error.WriteLine("click: no such window to address");
            return false;
        }

        var point = new POINT { X = screenX, Y = screenY };
        if (!ScreenToClient(hWnd, ref point))
        {
            Console.Error.WriteLine("click: the point could not be converted to client coordinates");
            return false;
        }

        var lParam = (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));

        // The two modes differ in whether the probe waits for Live's thread, not in what is
        // reported: both now answer "did the message reach that window".
        var down = useSend
            ? Send(hWnd, WmLButtonDown, (IntPtr)0x0001, lParam, "WM_LBUTTONDOWN")
            : Post(hWnd, WmLButtonDown, (IntPtr)0x0001, lParam, "WM_LBUTTONDOWN");

        Thread.Sleep(60);

        var up = useSend
            ? Send(hWnd, WmLButtonUp, IntPtr.Zero, lParam, "WM_LBUTTONUP")
            : Post(hWnd, WmLButtonUp, IntPtr.Zero, lParam, "WM_LBUTTONUP");

        return down && up;
    }

    public static bool Escape(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
        {
            Console.Error.WriteLine("escape: no such window to address");
            return false;
        }

        var down = Post(hWnd, WmKeyDown, (IntPtr)0x1B, (IntPtr)0x00000001, "WM_KEYDOWN(VK_ESCAPE)");
        Thread.Sleep(30);
        var up = Post(hWnd, WmKeyUp, (IntPtr)0x1B, unchecked((IntPtr)(long)0xC0000001), "WM_KEYUP(VK_ESCAPE)");

        return down && up;
    }

    private static bool Post(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, string what)
    {
        var queued = PostMessage(hWnd, msg, wParam, lParam);
        Console.WriteLine($"  post {what}: {(queued ? "queued" : $"NOT queued (Win32Error {Marshal.GetLastWin32Error()})")}");
        return queued;
    }

    private static bool Send(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, string what)
    {
        var answered = SendMessageTimeout(hWnd, msg, wParam, lParam, SmtoAbortIfHung, TimeoutMilliseconds, out var result);
        Console.WriteLine($"  send {what}: {(answered != IntPtr.Zero ? "delivered" : "NOT delivered")} window-procedure-result={result.ToInt64()}");
        return answered != IntPtr.Zero;
    }
}

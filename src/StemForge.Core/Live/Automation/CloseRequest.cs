namespace StemForge.Core.Live.Automation;

/// <summary>Asks a window to close the way the title-bar X would, without killing the process.</summary>
public static class CloseRequest
{
    public static bool CloseWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd)) return false;
        return NativeMethods.PostMessageW(hWnd, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);
    }
}

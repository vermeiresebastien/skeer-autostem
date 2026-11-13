using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StemForge.App.Theme;

/// <summary>
/// Paints the Windows title bar in the SKEER palette so the window chrome matches the
/// app instead of the system accent colour. This uses the Windows 11 DWM attributes; on
/// Windows 10 the calls fail harmlessly and the default chrome is kept.
/// </summary>
public static class WindowChrome
{
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    // SKEER ivory surface and charcoal ink, as 0x00BBGGRR.
    private const int IvoryCaption = 0x00EBF2F5;
    private const int CharcoalText = 0x00212829;
    private const int StoneBorder = 0x00BCD8CF;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplySkeerChrome(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            Set(handle, DwmwaCaptionColor, IvoryCaption);
            Set(handle, DwmwaTextColor, CharcoalText);
            Set(handle, DwmwaBorderColor, StoneBorder);
        }
        catch
        {
            // Unsupported platform or no DWM: keep the default chrome.
        }
    }

    private static void Set(IntPtr handle, int attribute, int colorRef)
    {
        var value = colorRef;
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }
}

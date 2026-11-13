using System.Drawing;
using System.Drawing.Imaging;

namespace StemForge.Core.Live.Automation;

/// <summary>
/// Captures the screen so the runner can verify visually where it is and leave
/// evidence behind when something unexpected happens. Captures are saved into the log
/// folder and referenced from the run log.
/// </summary>
public static class ScreenCapture
{
    public static Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static Bitmap CaptureWindow(WindowRect rect) =>
        CaptureRegion(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>Captures the whole virtual desktop (used when diagnosing focus problems).</summary>
    public static Bitmap CaptureDesktop()
    {
        var left = 0; var top = 0; var right = 0; var bottom = 0;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            left = Math.Min(left, screen.Bounds.Left);
            top = Math.Min(top, screen.Bounds.Top);
            right = Math.Max(right, screen.Bounds.Right);
            bottom = Math.Max(bottom, screen.Bounds.Bottom);
        }
        return CaptureRegion(left, top, right - left, bottom - top);
    }

    public static string Save(Bitmap bitmap, string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>Saves a diagnostic PNG of the window and returns its path.</summary>
    public static string? TrySaveWindow(IntPtr hWnd, string folder, string name)
    {
        try
        {
            var rect = WindowControl.GetRect(hWnd);
            if (rect is null) return null;
            using var bitmap = CaptureWindow(rect);
            return Save(bitmap, folder, name);
        }
        catch
        {
            return null;
        }
    }
}

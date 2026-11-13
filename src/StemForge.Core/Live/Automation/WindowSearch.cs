using System.Text;

namespace StemForge.Core.Live.Automation;

public sealed record FoundWindow(IntPtr Handle, int ProcessId, string Title, string ClassName);

/// <summary>Locates top-level windows by title, across all processes.</summary>
public static class WindowSearch
{
    public static IReadOnlyList<FoundWindow> All()
    {
        var result = new List<FoundWindow>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
            result.Add(new FoundWindow(hWnd, (int)pid, NativeMethods.GetTitle(hWnd), NativeMethods.GetClassName(hWnd)));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>Visible windows whose title contains <paramref name="substring"/>.</summary>
    public static IReadOnlyList<FoundWindow> FindByTitle(string substring, bool visibleOnly = true)
    {
        return All()
            .Where(w => w.Title.Contains(substring, StringComparison.OrdinalIgnoreCase))
            .Where(w => !visibleOnly || NativeMethods.IsWindowVisible(w.Handle))
            .ToList();
    }

    public static FoundWindow? FirstByTitle(string substring, bool visibleOnly = true) =>
        FindByTitle(substring, visibleOnly).FirstOrDefault();
}

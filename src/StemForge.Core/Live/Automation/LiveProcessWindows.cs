using System.Diagnostics;
using static StemForge.Core.Live.Automation.NativeMethods;

namespace StemForge.Core.Live.Automation;

public sealed record TopLevelWindow(IntPtr Handle, string Title, string ClassName, bool Visible)
{
    public override string ToString() => $"{Handle:X} '{Title}' [{ClassName}] visible={Visible}";
}

/// <summary>Enumerates the top-level windows a Live process owns (main window plus any dialogs).</summary>
public static class LiveProcessWindows
{
    public static IReadOnlyList<TopLevelWindow> ForProcess(int processId)
    {
        var result = new List<TopLevelWindow>();

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == (uint)processId)
            {
                result.Add(new TopLevelWindow(
                    hWnd,
                    GetTitle(hWnd),
                    GetClassName(hWnd),
                    IsWindowVisible(hWnd)));
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public static TopLevelWindow? MainWindow(int processId)
    {
        var windows = ForProcess(processId);
        return windows
            .Where(w => w.Visible && !string.IsNullOrWhiteSpace(w.Title))
            .OrderByDescending(w => w.Title.Length)
            .FirstOrDefault();
    }

    /// <summary>Visible top-level windows other than the given one (candidate dialogs).</summary>
    public static IReadOnlyList<TopLevelWindow> OtherVisibleWindows(int processId, IntPtr except)
    {
        return ForProcess(processId)
            .Where(w => w.Visible && w.Handle != except && !string.IsNullOrWhiteSpace(w.Title))
            .ToList();
    }

    public static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<Process> FindLiveProcesses()
    {
        try
        {
            return Process.GetProcesses()
                .Where(p =>
                {
                    try
                    {
                        return p.ProcessName.StartsWith("Ableton Live", StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();
        }
        catch
        {
            return Array.Empty<Process>();
        }
    }
}

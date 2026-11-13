using System.Diagnostics;
using Microsoft.Win32;

namespace StemForge.Core.Live;

/// <summary>
/// Finds Ableton Live installations on this machine. Nothing is hard-coded to one
/// machine: registry uninstall entries, the standard Ableton folders (including the
/// hidden ".Live 12 Suite_updated" style update folders Live creates) and the path of
/// a running Live process are all considered, and the newest build wins.
/// </summary>
public static class LiveLocator
{
    public static IReadOnlyList<LiveInstall> FindAll()
    {
        var candidates = new Dictionary<string, LiveInstall>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateCandidateExecutables())
        {
            var install = Describe(path);
            if (install is not null && !candidates.ContainsKey(install.ExePath))
            {
                candidates[install.ExePath] = install;
            }
        }

        return candidates.Values
            .OrderByDescending(i => i.ParsedVersion ?? new Version(0, 0))
            .ThenBy(i => i.ExePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static LiveInstall? Locate() => FindAll().FirstOrDefault();

    public static LiveInstall? DescribeExisting(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return null;
            return Describe(exePath);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateCandidateExecutables()
    {
        foreach (var p in FromRegistry()) yield return p;
        foreach (var p in FromAbletonFolders()) yield return p;
        foreach (var p in FromStandardProgramFolders()) yield return p;
        foreach (var p in FromRunningProcesses()) yield return p;
    }

    private static IEnumerable<string> FromRegistry()
    {
        var keys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Ableton",
        };

        foreach (var subKey in keys)
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                string[] names;
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default)
                        .OpenSubKey(subKey);
                    if (baseKey is null) continue;
                    names = baseKey.GetSubKeyNames();
                }
                catch
                {
                    continue;
                }

                foreach (var name in names)
                {
                    string? location = null;
                    string? display = null;
                    try
                    {
                        using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default)
                            .OpenSubKey(subKey + "\\" + name);
                        if (key is null) continue;
                        display = key.GetValue("DisplayName") as string;
                        location = key.GetValue("InstallLocation") as string;
                    }
                    catch
                    {
                        continue;
                    }

                    if (display is null || !display.Contains("Ableton", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location)) continue;

                    foreach (var exe in SafeEnumerateAudioExecutables(location))
                        yield return exe;
                }
            }
        }
    }

    private static IEnumerable<string> FromAbletonFolders()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ableton"),
            @"C:\ProgramData\Ableton",
        }.Distinct();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            string[] subdirs;
            try
            {
                // Include hidden/updated installs such as ".Live 12 Suite_updated".
                subdirs = Directory.GetDirectories(root);
            }
            catch
            {
                continue;
            }

            var searchDirs = subdirs.Append(root);
            foreach (var dir in searchDirs)
            {
                foreach (var exe in SafeEnumerateAudioExecutables(dir))
                    yield return exe;
            }
        }
    }

    private static IEnumerable<string> FromStandardProgramFolders()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            var ableton = Path.Combine(root, "Ableton");
            if (!Directory.Exists(ableton)) continue;

            foreach (var exe in SafeEnumerateAudioExecutables(ableton))
                yield return exe;
        }
    }

    private static IEnumerable<string> FromRunningProcesses()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("Ableton Live 12 Suite")
                .Concat(Process.GetProcessesByName("Ableton Live 12 Standard"))
                .Concat(Process.GetProcessesByName("Ableton Live 12 Intro"))
                .Concat(Process.GetProcessesByName("Ableton Live 11 Suite"))
                .ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var p in processes)
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* access denied is fine */ }
            p.Dispose();
            if (!string.IsNullOrEmpty(path)) yield return path;
        }
    }

    private static IEnumerable<string> SafeEnumerateAudioExecutables(string root)
    {
        const int maxDepth = 4;
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (depth > maxDepth) continue;

            string[] files;
            string[] dirs;
            try
            {
                files = Directory.GetFiles(dir, "Ableton Live*.exe");
                dirs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                // Ignore installers and helper executables.
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Contains("Installer", StringComparison.OrdinalIgnoreCase)) continue;
                yield return file;
            }

            foreach (var d in dirs)
            {
                // Never descend into Resources/Redist - they hold unrelated helpers.
                var leaf = Path.GetFileName(d);
                if (leaf.Equals("Resources", StringComparison.OrdinalIgnoreCase)) continue;
                if (leaf.Equals("Redist", StringComparison.OrdinalIgnoreCase)) continue;
                queue.Enqueue((d, depth + 1));
            }
        }
    }

    private static LiveInstall? Describe(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var version = info.FileVersion ?? info.ProductVersion ?? string.Empty;
            var product = info.ProductName ?? string.Empty;

            string edition = "Live";
            foreach (var candidate in new[] { "Suite", "Standard", "Intro", "Lite" })
            {
                if (product.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
                    exePath.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    edition = candidate;
                    break;
                }
            }

            return new LiveInstall(exePath, version, edition);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Locations to look for Live's Core Library (which ships the empty-audio-track
    /// template used to build our working project).
    /// </summary>
    public static IEnumerable<string> CoreLibraryRoots(LiveInstall install)
    {
        yield return Path.Combine(install.InstallRoot, "Resources", "Core Library");

        var abletonRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ableton");
        if (Directory.Exists(abletonRoot))
        {
            foreach (var dir in Directory.GetDirectories(abletonRoot))
                yield return Path.Combine(dir, "Resources", "Core Library");
        }
    }
}

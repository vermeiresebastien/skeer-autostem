using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StemForge.Core.Live;

/// <summary>
/// Finds Ableton Live installations on this machine. Nothing is hard-coded to one
/// machine: registry uninstall entries, the standard Ableton folders (including the
/// hidden ".Live 12 Suite_updated" style update folders Live creates) and the path of
/// a running Live process are all considered, and the newest build wins - unless a
/// normal install of its own sits beside it, in which case that one wins. See
/// <see cref="IsUpdateStagingPath"/>.
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

        return Rank(candidates.Values);
    }

    /// <summary>
    /// The order installs are tried in: a directory Ableton uses to stage an update is last,
    /// then the newest build, then the path. A staging copy only wins when nothing else exists,
    /// which is still better than refusing to start.
    /// </summary>
    public static IReadOnlyList<LiveInstall> Rank(IEnumerable<LiveInstall> installs) =>
        installs
            .OrderBy(i => IsUpdateStagingPath(i.ExePath))
            .ThenByDescending(i => i.ParsedVersion ?? new Version(0, 0))
            .ThenBy(i => i.ExePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// True for Ableton's own update-staging copy of an install - the install root a patch leaves
    /// behind, hidden as ".Live 12 Suite_updated" or named with an "_updated" suffix.
    ///
    /// It is a complete Live and usually the newer one, which is why plain version ordering used
    /// to pick it. Launching it is not free: its Core Library has never been imported into the
    /// database, so the main window comes up disabled - UI Automation reporting the whole subtree
    /// as two elements - and stays that way for as long as the import takes. A person who runs
    /// Live normally is running the directory without the dot.
    ///
    /// The name that decides this is the install root, two levels above the executable in
    /// Ableton's fixed `<root>\Program\<Live>.exe` layout - the same shape <see
    /// cref="LiveInstall.InstallRoot"/> assumes. Any hidden folder in the path would have caught
    /// things that are not Live installs at all.
    /// </summary>
    public static bool IsUpdateStagingPath(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;

        var programFolder = Path.GetDirectoryName(exePath);
        if (programFolder is null) return false;

        var installRoot = Path.GetDirectoryName(programFolder);
        if (installRoot is null) return false;

        var name = Path.GetFileName(installRoot);
        if (name.Length == 0) return false;

        return name[0] == '.' || name.EndsWith("_updated", StringComparison.OrdinalIgnoreCase);
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

            return new LiveInstall(exePath, version, ResolveEdition(product, exePath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Which edition a build names, from the two strings a version-info read actually yields plus the
    /// path it came from.
    ///
    /// The version info is asked first and, on the builds this desk has, is never the answer: Ableton
    /// writes ProductName "Live" for both the 12.3.2 install and its 12.3.5 staging copy (measured
    /// 2026-11-16). What does resolve "Suite" is the folder and exe name Ableton's own installer makes
    /// ("...\Live 12 Suite\Program\Ableton Live 12 Suite.exe"). This is therefore name matching, not a
    /// licence check - it reports what the file system chose to say, and an install moved off Ableton's
    /// naming says nothing, which is <see cref="LiveInstall.UnknownEdition"/>. The support gate accepts
    /// unknown on purpose; see <see cref="LiveInstall.EditionsWithoutStemSeparation"/>.
    ///
    /// A name counts when it is a word of its own, not a substring: the first candidate wins, so
    /// "Suite" is read ahead of the lesser products when a path happens to say both.
    /// </summary>
    public static string ResolveEdition(string? productName, string? exePath)
    {
        foreach (var candidate in LiveInstall.KnownEditions)
        {
            if (NamesEdition(candidate, productName) || NamesEdition(candidate, exePath)) return candidate;
        }

        return LiveInstall.UnknownEdition;
    }

    /// <summary>
    /// Whether a name says this edition as a word of its own - bounded by anything that is not a
    /// letter or a digit, which covers a space, a path separator and a dot alike. Substring matching
    /// is what this replaced, and it was not harmless once the edition became something the product
    /// refuses on: "Satellite Sessions", "Standards Library" and "Introduction to Production" all
    /// contain an edition name and none of them is one, and the answer here can be the difference
    /// between a batch that runs and a message saying the user's Live cannot do the job.
    /// </summary>
    private static bool NamesEdition(string edition, string? text) =>
        text is not null && Regex.IsMatch(
            text,
            @"(^|[^A-Za-z0-9])" + edition + @"([^A-Za-z0-9]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

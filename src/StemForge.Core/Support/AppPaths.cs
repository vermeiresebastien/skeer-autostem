namespace StemForge.Core.Support;

/// <summary>Well-known locations for the app's own state, logs and working areas.</summary>
public static class AppPaths
{
    /// <summary>
    /// Data root. Defaults to %LOCALAPPDATA%\SkeerAutoSTEM and can be redirected with the
    /// STEMFORGE_DATA_ROOT environment variable, which keeps tests (and portable runs)
    /// away from the real application data.
    /// </summary>
    public static string Root
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("STEMFORGE_DATA_ROOT");
            if (!string.IsNullOrWhiteSpace(overridden) && Path.IsPathRooted(overridden))
                return overridden;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SkeerAutoSTEM");
        }
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string QueueFile => Path.Combine(Root, "queue.json");
    public static string LogsFolder => Path.Combine(Root, "logs");
    public static string StagingRoot => Path.Combine(Root, "staging");

    /// <summary>Where per-job Live working projects live.</summary>
    public static string WorkRoot => Path.Combine(Root, "work");

    public static string WorkFolder(string jobId) => Path.Combine(WorkRoot, jobId);

    /// <summary>
    /// Where a Live session that was still open when a run started gets its unsaved work written
    /// to, under a name that cannot collide with a real project.
    /// </summary>
    public static string RecoveryRoot => Path.Combine(Root, "recovered");

    public static string LockFile => Path.Combine(Root, "runner.lock");

    /// <summary>Which Live process the last launch started, so orphans stay attributable.</summary>
    public static string LiveLaunchRecordFile => Path.Combine(Root, "live-launched.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsFolder);
    }
}

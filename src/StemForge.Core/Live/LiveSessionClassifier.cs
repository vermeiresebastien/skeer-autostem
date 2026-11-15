namespace StemForge.Core.Live;

public enum LiveSessionKind
{
    /// <summary>No window title yet: Live is still starting up.</summary>
    Starting,

    /// <summary>Our working set is open in it.</summary>
    OwnWorkingSet,

    /// <summary>A Live this tool launched that never got as far as loading the working set.</summary>
    OwnOrphan,

    /// <summary>A session that belongs to the user and must never be touched.</summary>
    Foreign,
}

/// <summary>
/// Decides what an already-running Ableton Live means for a batch. The rule is ownership, not
/// appearance: only a session this tool started itself may be closed, because a Live we
/// launched cannot hold the user's unsaved work. Everything else is the user's, including an
/// empty "Untitled" set, which is indistinguishable by title from unsaved arranging.
/// </summary>
public static class LiveSessionClassifier
{
    /// <summary>Marker every working set carries, in the file name and therefore the title.</summary>
    public const string WorkingSetMarker = "autoSTEM";

    public static LiveSessionKind Classify(string title, bool launchedByUs)
    {
        if (string.IsNullOrWhiteSpace(title)) return LiveSessionKind.Starting;
        if (title.Contains(WorkingSetMarker, StringComparison.OrdinalIgnoreCase)) return LiveSessionKind.OwnWorkingSet;
        if (launchedByUs && title.StartsWith("Untitled", StringComparison.OrdinalIgnoreCase)) return LiveSessionKind.OwnOrphan;

        return LiveSessionKind.Foreign;
    }
}

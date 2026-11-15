using System.Diagnostics;
using System.Text.Json;
using StemForge.Core.Support;

namespace StemForge.Core.Live;

/// <summary>
/// Remembers the Live process the last launch started. A run that aborts between launching
/// Live and loading the working set leaves an instance open that a later run would otherwise
/// mistake for the user's session; the record is what keeps that orphan attributable.
/// </summary>
public sealed record LiveLaunchRecord(int ProcessId, long StartedUtcTicks, string ExePath)
{
    public static void Write(Process process, string exePath)
    {
        try
        {
            var record = new LiveLaunchRecord(
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                exePath);

            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(AppPaths.LiveLaunchRecordFile, JsonSerializer.Serialize(record));
        }
        catch
        {
            // The record only sharpens a later preflight; losing it must never fail a run.
        }
    }

    public static void Clear()
    {
        try
        {
            File.Delete(AppPaths.LiveLaunchRecordFile);
        }
        catch
        {
            // A stale record is harmless: ReadCurrent validates it against the live process.
        }
    }

    /// <summary>The record while it still describes a running Live, otherwise null.</summary>
    public static LiveLaunchRecord? ReadCurrent()
    {
        try
        {
            var record = JsonSerializer.Deserialize<LiveLaunchRecord>(File.ReadAllText(AppPaths.LiveLaunchRecordFile));
            if (record is null) return null;

            using var process = Process.GetProcessById(record.ProcessId);
            if (process.HasExited) return null;
            if (!process.ProcessName.StartsWith("Ableton Live", StringComparison.OrdinalIgnoreCase)) return null;
            if (process.StartTime.ToUniversalTime().Ticks != record.StartedUtcTicks) return null;

            return record;
        }
        catch
        {
            return null;
        }
    }
}

using System.Text;
using StemForge.Core.Models;
using StemForge.Core.Support;

namespace StemForge.Core.Logging;

public enum LogLevel { Info, Warn, Error, Stage }

public sealed record LogEntry(DateTime Utc, LogLevel Level, string? JobId, string Message);

/// <summary>
/// Append-only run log with timestamps, job stages, results and failure reasons.
/// Writes to %LOCALAPPDATA%\StemForge\logs\run-*.log and raises an event so the UI
/// can show the same lines live. The file path is surfaced in the app so users can
/// find it after an overnight run.
/// </summary>
public sealed class RunLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public string FilePath { get; }

    public event EventHandler<LogEntry>? EntryWritten;

    public RunLog(string? filePath = null)
    {
        Directory.CreateDirectory(AppPaths.LogsFolder);
        FilePath = filePath ?? Path.Combine(
            AppPaths.LogsFolder,
            $"run-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..30] + ".log");

        _writer = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
    }

    public void Info(string message, Job? job = null) => Write(LogLevel.Info, message, job);
    public void Warn(string message, Job? job = null) => Write(LogLevel.Warn, message, job);
    public void Error(string message, Job? job = null) => Write(LogLevel.Error, message, job);

    public void Stage(Job job, JobStage stage, string? detail = null)
    {
        var message = detail is null
            ? stage.DisplayName()
            : $"{stage.DisplayName()} - {detail}";
        Write(LogLevel.Stage, message, job);
    }

    private void Write(LogLevel level, string message, Job? job)
    {
        var entry = new LogEntry(DateTime.UtcNow, level, job?.Id, message);
        var line = Format(entry);
        lock (_gate)
        {
            _writer.WriteLine(line);
        }

        EntryWritten?.Invoke(this, entry);
    }

    private static string Format(LogEntry e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Utc.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append(" [").Append(e.Level.ToString().ToUpperInvariant().PadRight(5)).Append("] ");
        if (!string.IsNullOrEmpty(e.JobId))
        {
            sb.Append(e.JobId);
            sb.Append(' ');
        }
        sb.Append(e.Message);
        return sb.ToString();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _writer.Dispose(); } catch { /* ignore */ }
        }
    }
}

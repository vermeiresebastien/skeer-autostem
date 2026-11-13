using StemForge.Core.Models;
using StemForge.Core.Support;

namespace StemForge.Core.Queue;

public sealed class QueueState
{
    public int SchemaVersion { get; set; } = 1;
    public List<Job> Jobs { get; set; } = new();
}

/// <summary>
/// Durable queue: every job is written to disk, so a crash, a reboot or an overnight
/// run interrupted by Windows update never loses the batch.
/// </summary>
public sealed class QueueStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public QueueStore(string? path = null) => _path = path ?? AppPaths.QueueFile;

    public string FilePath => _path;

    /// <summary>
    /// Loads the queue and returns jobs left in <see cref="JobStatus.Running"/> by a
    /// previous session back to a retryable state.
    /// </summary>
    public List<Job> Load()
    {
        QueueState state;
        lock (_gate)
        {
            state = JsonStore.Load<QueueState>(_path) ?? new QueueState();
        }

        foreach (var job in state.Jobs)
        {
            if (job.Status == JobStatus.Running)
            {
                job.Status = JobStatus.Pending;
                job.Stage = JobStage.Queued;
                job.Error = "Interrupted - returned to the queue and will be retried.";
                job.ErrorKind = ErrorKind.None;
            }
        }

        return state.Jobs;
    }

    public void Save(IEnumerable<Job> jobs)
    {
        var state = new QueueState { Jobs = jobs.ToList() };
        lock (_gate)
        {
            JsonStore.Save(_path, state);
        }
    }
}

/// <summary>User preferences store (output folder, last mode/stems, Live path).</summary>
public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string? path = null) => _path = path ?? AppPaths.SettingsFile;

    public string FilePath => _path;

    public AppSettings Load() => JsonStore.Load<AppSettings>(_path) ?? new AppSettings();

    public void Save(AppSettings settings) => JsonStore.Save(_path, settings);
}

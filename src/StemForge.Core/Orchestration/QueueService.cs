using StemForge.Core.Media;
using StemForge.Core.Models;
using StemForge.Core.Queue;

namespace StemForge.Core.Orchestration;

/// <summary>
/// Owns the queue: adding files and folders, de-duplicating, removing, clearing and
/// retrying — and freezing the stem/mode/destination settings onto each job so the
/// runner never has to consult global state.
/// </summary>
public sealed class QueueService
{
    private readonly QueueStore _store;
    private readonly List<Job> _jobs = new();

    public QueueService(QueueStore store) => _store = store;

    public IReadOnlyList<Job> Jobs => _jobs;

    public string QueueFilePath => _store.FilePath;

    public void Load() => Replace(_store.Load());

    public void Save() => _store.Save(_jobs);

    private void Replace(IEnumerable<Job> jobs)
    {
        _jobs.Clear();
        _jobs.AddRange(jobs);
    }

    /// <summary>
    /// Adds files and folders. Folders contribute supported audio files only; the caller
    /// decides whether subfolders are included (recursive import is explicit in the UI).
    /// Returns the number of new jobs added.
    /// </summary>
    public int AddPaths(IEnumerable<string> paths, bool recursive, JobProfile profile, string destinationRoot)
    {
        var added = 0;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                added += AddFolder(path, recursive, profile, destinationRoot);
            }
            else if (File.Exists(path))
            {
                added += AddFile(path, profile, destinationRoot) ? 1 : 0;
            }
        }

        if (added > 0) Save();
        return added;
    }

    private int AddFolder(string folder, bool recursive, JobProfile profile, string destinationRoot)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var added = 0;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", option);
        }
        catch
        {
            return 0;
        }

        foreach (var file in files)
        {
            // Folder import is filtered to supported audio; unsupported files are ignored
            // rather than queued purely to fail.
            if (!AudioFileTypes.IsSupported(file)) continue;
            if (AddFile(file, profile, destinationRoot)) added++;
        }

        return added;
    }

    private bool AddFile(string path, JobProfile profile, string destinationRoot)
    {
        SourceIdentity identity;
        try
        {
            identity = SourceIdentity.Capture(path);
        }
        catch
        {
            return false;
        }

        var id = JobId.Create(identity, profile, destinationRoot);
        if (_jobs.Any(j => j.Id == id)) return false;

        _jobs.Add(new Job
        {
            Id = id,
            SourcePath = identity.Path,
            SourceSizeBytes = identity.SizeBytes,
            SourceLastWriteUtcTicks = identity.LastWriteUtcTicks,
            SourceQuickHash = identity.QuickHash,
            Stems = profile.Stems,
            Mode = profile.Mode,
            DestinationRoot = destinationRoot,
            Status = JobStatus.Pending,
            Stage = JobStage.Queued,
        });

        return true;
    }

    public bool Remove(string jobId)
    {
        var job = _jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null) return false;

        _jobs.Remove(job);
        Save();
        return true;
    }

    /// <summary>Removes every finished job from the queue (files on disk are untouched).</summary>
    public int ClearCompleted()
    {
        var removed = _jobs.RemoveAll(j => j.Status is JobStatus.Completed or JobStatus.Skipped);
        if (removed > 0) Save();
        return removed;
    }

    /// <summary>Puts failed jobs back in the queue without changing their recorded settings.</summary>
    public int RetryFailed()
    {
        var changed = 0;
        foreach (var job in _jobs.Where(j => j.Status == JobStatus.Failed))
        {
            job.Reset();
            changed++;
        }

        if (changed > 0) Save();
        return changed;
    }

    /// <summary>
    /// Applies the current stem selection, processing mode and destination to every job
    /// that has not finished yet, and re-keys their ids so the new settings are part of
    /// their identity. Called when Run is pressed.
    /// </summary>
    public void FreezeSettings(JobProfile profile, string destinationRoot)
    {
        var keep = new List<Job>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var job in _jobs)
        {
            if (job.Status is JobStatus.Completed or JobStatus.Skipped)
            {
                keep.Add(job);
                seen.Add(job.Id);
                continue;
            }

            job.Stems = profile.Stems;
            job.Mode = profile.Mode;
            job.DestinationRoot = destinationRoot;
            job.Id = JobId.Create(job.Source, job.Profile, destinationRoot);
            job.Touch();

            if (!seen.Add(job.Id)) continue;    // an identical job already exists
            keep.Add(job);
        }

        Replace(keep);
        Save();
    }

    /// <summary>Jobs that still need processing, in queue order.</summary>
    public IReadOnlyList<Job> Runnable() => _jobs
        .Where(j => j.Status is JobStatus.Pending or JobStatus.Running or JobStatus.Failed)
        .ToList();

    public Job? Find(string jobId) => _jobs.FirstOrDefault(j => j.Id == jobId);
}

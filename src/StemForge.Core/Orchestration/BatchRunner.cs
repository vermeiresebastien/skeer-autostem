using StemForge.Core.Audio;
using StemForge.Core.Engines;
using StemForge.Core.Interop;
using StemForge.Core.Live;
using StemForge.Core.Logging;
using StemForge.Core.Media;
using StemForge.Core.Models;
using StemForge.Core.Publish;
using StemForge.Core.Support;
using StemForge.Core.Verify;

namespace StemForge.Core.Orchestration;

public sealed record BatchProgress(
    string? JobId,
    string? TrackName,
    JobStage Stage,
    string? Detail,
    double? Percent,
    int Completed,
    int Skipped,
    int Failed,
    int Remaining,
    int Total,
    TimeSpan Elapsed,
    string? Message = null);

public sealed record BatchSummary(
    int Completed,
    int Skipped,
    int Failed,
    int Remaining,
    TimeSpan Duration,
    bool Stopped,
    ErrorKind StopReason,
    string? StopMessage,
    string LogPath);

public sealed class BatchContext
{
    public required AppSettings Settings { get; init; }
    public required LiveInstall Install { get; init; }
    public required string BaseTemplatePath { get; init; }
    public required RunLog Log { get; init; }

    /// <summary>Called whenever a job changes state so the queue can be flushed to disk.</summary>
    public Action? PersistQueue { get; init; }

    public IProgress<BatchProgress>? Progress { get; init; }
}

/// <summary>
/// Processes the queue one track at a time. Everything the UI shows comes from here, and
/// every state transition is persisted so an interrupted batch resumes exactly where it
/// stopped with the settings each job was created with.
/// </summary>
public sealed class BatchRunner
{
    private readonly ISeparator _runner;

    public BatchRunner(ISeparator runner) => _runner = runner;

    /// <summary>
    /// When set, the run stops after the job currently being processed has been finished
    /// and verified. The flag is only read between jobs, so it never aborts a running track.
    /// </summary>
    public bool StopAfterCurrent { get; set; }

    public async Task<BatchSummary> RunAsync(IReadOnlyList<Job> jobs, BatchContext context, CancellationToken cancellationToken)
    {
        var log = context.Log;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var completed = jobs.Count(j => j.Status == JobStatus.Completed);
        var skipped = jobs.Count(j => j.Status == JobStatus.Skipped);
        var failed = jobs.Count(j => j.Status == JobStatus.Failed);

        var stopReason = ErrorKind.None;
        string? stopMessage = null;
        var stopped = false;

        log.Info($"--- run started: {jobs.Count} job(s), engine = {_runner.Name} ---");

        using var awake = context.Settings.KeepAwakeDuringRun ? new SystemAwake() : null;
        if (context.Settings.KeepAwakeDuringRun)
        {
            log.Info(awake is { Active: true }
                ? "requested that Windows stay awake for the duration of this run"
                : $"could not request the keep-awake state ({awake?.FailureReason})");
        }

        try
        {
            foreach (var job in jobs)
            {
                if (cancellationToken.IsCancellationRequested) { stopped = true; break; }

                if (StopAfterCurrent)
                {
                    stopped = true;
                    stopMessage = "Stopped after the current track, as requested.";
                    log.Info(stopMessage);
                    break;
                }

                if (job.Status is JobStatus.Completed or JobStatus.Skipped)
                {
                    // A finished job is only skipped while its verified outputs still exist.
                    if (OutputPublisher.IsAlreadySatisfied(job, out _)) continue;

                    log.Warn("previous result is missing or no longer verified; reprocessing", job);
                    job.Reset("Previous result is missing or no longer verified; reprocessing.");
                    context.PersistQueue?.Invoke();
                }

                var outcome = await ProcessJobAsync(job, context, cancellationToken).ConfigureAwait(false);

                switch (job.Status)
                {
                    case JobStatus.Completed: completed++; break;
                    case JobStatus.Skipped: skipped++; break;
                    case JobStatus.Failed: failed++; break;
                }

                context.PersistQueue?.Invoke();
                Report(context, job, job.Stage, job.Error, null, completed, skipped, failed, jobs);

                if (outcome is { Continue: false })
                {
                    stopped = true;
                    stopReason = outcome.Kind;
                    stopMessage = outcome.Message;
                    log.Warn($"batch paused: {outcome.Message}");
                    break;
                }
            }
        }
        finally
        {
            stopwatch.Stop();
            log.Info($"--- run finished in {stopwatch.Elapsed:hh\\:mm\\:ss} " +
                     $"(completed {completed}, skipped {skipped}, failed {failed}) ---");
        }

        var remaining = jobs.Count(j => j.Status is JobStatus.Pending or JobStatus.Running);

        return new BatchSummary(completed, skipped, failed, remaining, stopwatch.Elapsed, stopped,
            stopReason, stopMessage, log.FilePath);
    }

    private sealed record JobStep(bool Continue, ErrorKind Kind, string? Message);

    private async Task<JobStep> ProcessJobAsync(Job job, BatchContext context, CancellationToken cancellationToken)
    {
        var log = context.Log;
        job.Attempts++;
        job.Reset();

        // --- source validation -------------------------------------------------
        job.Status = JobStatus.Running;
        job.Stage = JobStage.CheckingSource;
        job.Touch();
        context.PersistQueue?.Invoke();
        Report(context, job, JobStage.CheckingSource, null, null, 0, 0, 0, null);

        log.Info($"job {job.Id}: {job.DisplayName} ({job.Profile.Descriptor})", job);

        if (!File.Exists(job.SourcePath))
            return Fail(job, context, ErrorKind.SourceMissing,
                $"The source file is missing: {job.SourcePath}", continueBatch: true);

        if (!AudioFileTypes.IsSupported(job.SourcePath))
            return Fail(job, context, ErrorKind.UnsupportedFormat,
                $"Unsupported file type: {Path.GetExtension(job.SourcePath).ToLowerInvariant()}.", continueBatch: true);

        var current = SafeCaptureIdentity(job.SourcePath);
        if (current is null)
            return Fail(job, context, ErrorKind.SourceUnreadable,
                "The source file could not be read.", continueBatch: true);

        if (current.SizeBytes != job.SourceSizeBytes ||
            current.LastWriteUtcTicks != job.SourceLastWriteUtcTicks ||
            !string.Equals(current.QuickHash, job.SourceQuickHash, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(job, context, ErrorKind.SourceChanged,
                "The source file has changed since it was queued. Add it again to process the new version.",
                continueBatch: true);
        }

        if (OutputPublisher.IsAlreadySatisfied(job, out var existing))
        {
            job.Status = JobStatus.Skipped;
            job.Stage = JobStage.Skipped;
            job.Error = null;
            job.ErrorKind = ErrorKind.None;
            job.Touch();
            log.Info($"already satisfied by {existing}", job);
            return new JobStep(true, ErrorKind.None, null);
        }

        WavInfo sourceInfo;
        try
        {
            sourceInfo = AudioFileReader.ReadInfo(job.SourcePath);
        }
        catch (Exception ex)
        {
            return Fail(job, context, ErrorKind.UnsupportedFormat,
                $"The source file could not be read: {ex.Message}", continueBatch: true);
        }

        // --- run the engine ----------------------------------------------------
        var working = AppPaths.WorkFolder(job.Id);
        ResetWorkingFolder(working);

        var options = context.Settings;
        var request = new SeparationRequest
        {
            Job = job,
            Profile = job.Profile,
            Install = context.Install,
            Source = current,
            SourceInfo = sourceInfo,
            WorkingFolder = working,
            BaseTemplatePath = context.BaseTemplatePath,
            Timeout = TimeSpan.FromMinutes(options.MaxSeparationMinutes),
            Progress = new Progress<SeparationProgress>(p =>
                Report(context, job, p.Stage, p.Detail, p.Percent, 0, 0, 0, null)),
        };

        SeparationOutcome outcome;
        try
        {
            outcome = await _runner.SeparateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            job.Reset("Stopped before finishing; it will be retried.");
            context.PersistQueue?.Invoke();
            return new JobStep(true, ErrorKind.Cancelled, null);
        }

        if (!outcome.Success)
        {
            // Keep anything that actually verified before the failure - labelled partial,
            // never counted as finished output.
            if (outcome.GeneratedFiles.Count > 0)
            {
                var partialOptions = new VerificationOptions
                {
                    DurationToleranceFraction = options.DurationToleranceFraction,
                    DurationToleranceFloorSeconds = options.DurationToleranceFloorSeconds,
                };

                OutputPublisher.PublishPartial(
                    job, outcome.GeneratedFiles, sourceInfo, partialOptions,
                    context.Install.Version, outcome.Error ?? "separation failed",
                    m => log.Info(m, job));
            }

            var kind = EngineFailures.Normalize(_runner.Kind, outcome.Kind);
            if (kind != outcome.Kind)
                log.Warn($"engine '{_runner.Name}' reported the Live-only failure {outcome.Kind}; " +
                         $"recorded as {kind}", job);

            var pause = ShouldPauseBatch(kind);
            return Fail(job, context, kind, outcome.Error ?? "Separation failed.", continueBatch: !pause);
        }

        // --- verify the source was left alone ---------------------------------
        var after = SafeCaptureIdentity(job.SourcePath);
        if (after is not null && after.QuickHash != current.QuickHash)
        {
            log.Warn("the source file changed during processing", job);
            return Fail(job, context, ErrorKind.SourceChanged,
                "The source file was modified while it was being processed. The result was discarded.", continueBatch: true);
        }

        // --- verify and publish ------------------------------------------------
        job.Stage = JobStage.VerifyingStems;
        job.Touch();
        context.PersistQueue?.Invoke();

        var verification = new VerificationOptions
        {
            DurationToleranceFraction = options.DurationToleranceFraction,
            DurationToleranceFloorSeconds = options.DurationToleranceFloorSeconds,
        };

        job.Stage = JobStage.Publishing;
        job.Touch();

        var publish = OutputPublisher.Publish(
            job, outcome.GeneratedFiles, sourceInfo, verification,
            context.Install.Version, m => log.Info(m, job));

        job.LastSeparationSeconds = outcome.SeparationSeconds;

        if (!publish.Ok)
        {
            var pause = ShouldPauseBatch(publish.Kind);
            return Fail(job, context, publish.Kind, publish.Error ?? "Publishing failed.", continueBatch: !pause);
        }

        job.Status = JobStatus.Completed;
        job.Stage = JobStage.Completed;
        job.Error = null;
        job.ErrorKind = ErrorKind.None;
        job.ResultFolder = Path.GetFileName(publish.ResultFolder);
        job.OutputFiles = publish.Files.Select(f => Path.GetFileName(f) ?? f).ToList();
        job.PartialFiles.Clear();
        job.PartialFolder = null;
        job.Touch();

        log.Info($"published {publish.Files.Count} stem(s) to {publish.ResultFolder} " +
                 $"(separation {outcome.SeparationSeconds:F1}s)", job);
        return new JobStep(true, ErrorKind.None, null);
    }

    private JobStep Fail(Job job, BatchContext context, ErrorKind kind, string message, bool continueBatch)
    {
        job.Fail(kind, message);
        context.Log.Error($"{kind}: {message}", job);
        context.PersistQueue?.Invoke();
        return new JobStep(continueBatch, kind, message);
    }

    /// <summary>
    /// Failures that leave Live or the destination in an unknown state pause the whole
    /// batch instead of blindly continuing; the queue is preserved either way.
    /// </summary>
    private static bool ShouldPauseBatch(ErrorKind kind) => kind switch
    {
        ErrorKind.SourceMissing => false,
        ErrorKind.SourceChanged => false,
        ErrorKind.UnsupportedFormat => false,
        ErrorKind.SourceUnreadable => false,
        ErrorKind.StemsMissing => false,
        ErrorKind.StemsUnverified => false,
        ErrorKind.PublishFailed => false,
        ErrorKind.SeparationCancelled => false,
        _ => true,
    };

    private static SourceIdentity? SafeCaptureIdentity(string path)
    {
        try
        {
            return SourceIdentity.Capture(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gives every job a clean working area so a previous run's files can never satisfy
    /// the current one. The whole folder goes, not just the Stems subfolder: the working
    /// area holds a Live project whose name Live depends on, and a half-cleared project is
    /// worse than none.
    /// </summary>
    private static void ResetWorkingFolder(string working)
    {
        try
        {
            if (Directory.Exists(working)) Directory.Delete(working, recursive: true);
            Directory.CreateDirectory(working);
        }
        catch
        {
            // If it cannot be cleared the runner's baseline snapshot still protects us.
        }
    }

    private static void Report(
        BatchContext context,
        Job? job,
        JobStage stage,
        string? detail,
        double? percent,
        int completed,
        int skipped,
        int failed,
        IReadOnlyList<Job>? jobs)
    {
        context.Progress?.Report(new BatchProgress(
            job?.Id,
            job?.DisplayName,
            stage,
            detail,
            percent,
            completed,
            skipped,
            failed,
            jobs?.Count(j => j.Status is JobStatus.Pending or JobStatus.Running) ?? 0,
            jobs?.Count ?? 0,
            TimeSpan.Zero));
    }
}

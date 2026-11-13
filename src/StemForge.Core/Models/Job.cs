using System.Text.Json.Serialization;

namespace StemForge.Core.Models;

/// <summary>
/// A single queue entry: everything needed to reproduce, resume and verify one
/// track's separation, persisted to disk on every transition.
/// </summary>
public sealed class Job
{
    /// <summary>Stable deterministic id (see <see cref="JobId"/>).</summary>
    public string Id { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;
    public long SourceSizeBytes { get; set; }
    public long SourceLastWriteUtcTicks { get; set; }
    public string SourceQuickHash { get; set; } = string.Empty;

    /// <summary>Stem selection, frozen when Run was pressed.</summary>
    public Stems Stems { get; set; } = Stems.All;

    /// <summary>Processing mode, frozen when Run was pressed.</summary>
    public ProcessingMode Mode { get; set; } = ProcessingMode.HighSpeed;

    /// <summary>Destination root the user chose for this job.</summary>
    public string DestinationRoot { get; set; } = string.Empty;

    public JobStatus Status { get; set; } = JobStatus.Pending;

    public JobStage Stage { get; set; } = JobStage.Queued;

    public string? Error { get; set; }

    public ErrorKind ErrorKind { get; set; } = ErrorKind.None;

    /// <summary>Folder the verified stems were published into (relative to the destination root).</summary>
    public string? ResultFolder { get; set; }

    /// <summary>Published file names inside <see cref="ResultFolder"/>.</summary>
    public List<string> OutputFiles { get; set; } = new();

    /// <summary>
    /// Verified stems salvaged from a failed attempt. Only populated when each file was
    /// actually verified, and never used to satisfy a job.
    /// </summary>
    public List<string> PartialFiles { get; set; } = new();

    /// <summary>Folder the partial stems were written to, relative to the destination root.</summary>
    public string? PartialFolder { get; set; }

    public int Attempts { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Wall-clock seconds spent inside Live's separation stage for the last attempt.</summary>
    public double? LastSeparationSeconds { get; set; }

    [JsonIgnore]
    public JobProfile Profile => new(Stems, Mode);

    [JsonIgnore]
    public string DisplayName => Path.GetFileName(SourcePath);

    [JsonIgnore]
    public bool IsTerminal => Status is JobStatus.Completed or JobStatus.Skipped or JobStatus.Failed;

    [JsonIgnore]
    public SourceIdentity Source => new(SourcePath, SourceSizeBytes, SourceLastWriteUtcTicks, SourceQuickHash);

    public void Touch() => UpdatedUtc = DateTime.UtcNow;

    public void Fail(ErrorKind kind, string message)
    {
        Status = JobStatus.Failed;
        Stage = JobStage.Failed;
        ErrorKind = kind;
        Error = message;
        Touch();
    }

    public void Reset(string message = "")
    {
        Status = JobStatus.Pending;
        Stage = JobStage.Queued;
        ErrorKind = ErrorKind.None;
        Error = string.IsNullOrEmpty(message) ? null : message;
        PartialFiles.Clear();
        PartialFolder = null;
        Touch();
    }
}

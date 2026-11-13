namespace StemForge.Core.Models;

/// <summary>
/// Coarse stages a job moves through. Reported to the UI so the user always knows
/// where the batch is, and written to the run log for later diagnosis.
/// </summary>
public enum JobStage
{
    Queued,
    CheckingSource,
    PreparingProject,
    LaunchingLive,
    WaitingForLive,
    OpeningProject,
    SelectingClip,
    OpeningStemDialog,
    ConfiguringStemDialog,
    Separating,
    CollectingStems,
    VerifyingStems,
    Publishing,
    Completed,
    Skipped,
    Failed,
}

public static class JobStageExtensions
{
    public static string DisplayName(this JobStage stage) => stage switch
    {
        JobStage.Queued => "Queued",
        JobStage.CheckingSource => "Checking source",
        JobStage.PreparingProject => "Preparing working project",
        JobStage.LaunchingLive => "Launching Ableton Live",
        JobStage.WaitingForLive => "Waiting for Live",
        JobStage.OpeningProject => "Opening working project",
        JobStage.SelectingClip => "Selecting clip",
        JobStage.OpeningStemDialog => "Opening Separate Stems",
        JobStage.ConfiguringStemDialog => "Configuring stems and mode",
        JobStage.Separating => "Separating stems",
        JobStage.CollectingStems => "Collecting generated audio",
        JobStage.VerifyingStems => "Verifying stems",
        JobStage.Publishing => "Publishing results",
        JobStage.Completed => "Completed",
        JobStage.Skipped => "Skipped",
        JobStage.Failed => "Failed",
        _ => stage.ToString(),
    };

    /// <summary>True while real separation work is happening (used for the elapsed timer).</summary>
    public static bool IsSeparationStage(this JobStage stage) =>
        stage is JobStage.Separating or JobStage.CollectingStems;
}

namespace StemForge.Core.Models;

/// <summary>
/// The frozen processing profile of a job: which stems were requested and in which
/// mode. Two jobs only satisfy each other when their profiles are identical, so a
/// High Quality / Drums Only result can never be reused for an All Stems request.
/// </summary>
public sealed record JobProfile(Stems Stems, ProcessingMode Mode)
{
    public static JobProfile Default { get; } = new(Stems.All, ProcessingMode.HighSpeed);

    /// <summary>Stable key persisted with each job and each result manifest.</summary>
    public string Key => $"{Stems}/{Mode}";

    /// <summary>Suffix used for result folder names; empty for the default profile.</summary>
    public string FolderSuffix => BuildSuffix(Stems, Mode);

    public string Descriptor => $"{Stems.Describe()} ({Mode.DisplayName()})";

    internal static string BuildSuffix(Stems stems, ProcessingMode mode)
    {
        var stemPart = stems.ProfileSuffix();
        var modePart = mode.ProfileSuffix();
        var parts = new[] { stemPart, modePart }.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        return parts.Length == 0 ? string.Empty : string.Join(", ", parts);
    }

    public override string ToString() => Key;
}

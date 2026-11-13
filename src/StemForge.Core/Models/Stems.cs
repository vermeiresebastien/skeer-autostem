namespace StemForge.Core.Models;

/// <summary>
/// The four stems Ableton Live can produce from a single audio file.
/// Used as a bit mask so a job can request any non-empty subset.
/// </summary>
[Flags]
public enum Stems
{
    None = 0,
    Vocals = 1 << 0,
    Drums = 1 << 1,
    Bass = 1 << 2,
    Others = 1 << 3,
    All = Vocals | Drums | Bass | Others,
}

public enum StemsPreset
{
    All,
    DrumsOnly,
    Custom,
}

public static class StemsExtensions
{
    /// <summary>Canonical display/processing order used everywhere (matches Live's dialog order).</summary>
    public static readonly Stems[] Order = { Stems.Bass, Stems.Drums, Stems.Vocals, Stems.Others };

    public static IReadOnlyList<Stems> Enumerate(this Stems stems) =>
        Order.Where(s => stems.HasFlag(s)).ToArray();

    public static int Count(this Stems stems) => Enum.GetValues<Stems>()
        .Where(s => s != Stems.None && s != Stems.All && stems.HasFlag(s))
        .Count();

    /// <summary>Output file name published for a single-stem request (matches the documented example).</summary>
    public static string OutputFileName(this Stems stem) => stem switch
    {
        Stems.Vocals => "vocals.wav",
        Stems.Drums => "drums.wav",
        Stems.Bass => "bass.wav",
        Stems.Others => "other.wav",
        _ => throw new ArgumentOutOfRangeException(nameof(stem), stem, "Not a single stem."),
    };

    /// <summary>Keyword Ableton uses for this stem in its UI / generated file names.</summary>
    public static string LiveKeyword(this Stems stem) => stem switch
    {
        Stems.Vocals => "vocals",
        Stems.Drums => "drums",
        Stems.Bass => "bass",
        Stems.Others => "others",
        _ => throw new ArgumentOutOfRangeException(nameof(stem), stem, "Not a single stem."),
    };

    public static string DisplayName(this Stems stem) => stem switch
    {
        Stems.Vocals => "Vocals",
        Stems.Drums => "Drums",
        Stems.Bass => "Bass",
        Stems.Others => "Others",
        _ => stem.ToString(),
    };

    /// <summary>Human readable description, e.g. "all stems", "drums only", "vocals + bass".</summary>
    public static string Describe(this Stems stems)
    {
        if (stems == Stems.All) return "all stems";
        var parts = stems.Enumerate().Select(s => s.DisplayName().ToLowerInvariant()).ToArray();
        if (parts.Length == 1) return $"{parts[0]} only";
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Short, filesystem-safe suffix used when a result folder must disambiguate
    /// different stem selections. Empty for the default (All) profile.
    /// </summary>
    public static string ProfileSuffix(this Stems stems)
    {
        if (stems == Stems.All) return string.Empty;
        return string.Join(", ", stems.Enumerate().Select(s => s.DisplayName()));
    }
}

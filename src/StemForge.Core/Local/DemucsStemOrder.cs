using StemForge.Core.Models;

namespace StemForge.Core.Local;

/// <summary>
/// Which slice of the model's output is which stem.
///
/// Not <see cref="StemsExtensions.Order"/>, which is Ableton's dialog order. Demucs answers with
/// its own source list - <c>["drums", "bass", "other", "vocals"]</c>, from
/// <c>demucs/pretrained.py</c> - and nothing in a float array reveals that, so the two indices
/// that are easiest to confuse, vocals and others, are the two that a careless mapping would
/// swap in silence. A bag of models (<c>htdemucs_ft</c>) keeps the same order for every member;
/// a six-source model does not, and is not supported until a mapping exists for it.
/// </summary>
public static class DemucsStemOrder
{
    /// <summary>Source index → stem, in the order a Demucs checkpoint returns them.</summary>
    public static readonly IReadOnlyList<Stems> FourSources = [Stems.Drums, Stems.Bass, Stems.Others, Stems.Vocals];

    public static Stems StemAt(int sourceIndex) => sourceIndex >= 0 && sourceIndex < FourSources.Count
        ? FourSources[sourceIndex]
        : throw new ArgumentOutOfRangeException(nameof(sourceIndex), "This model has more sources than the four-stem mapping.");

    /// <summary>
    /// The source indices to run for a request, in the model's own order, so an engine can skip
    /// the slices nobody asked for and still know what it is looking at.
    /// </summary>
    public static IReadOnlyList<int> IndicesFor(Stems requested)
    {
        var indices = new List<int>();
        for (var i = 0; i < FourSources.Count; i++)
        {
            if (requested.HasFlag(FourSources[i])) indices.Add(i);
        }

        return indices;
    }
}

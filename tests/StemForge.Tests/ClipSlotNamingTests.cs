using StemForge.Core.Live.Automation;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// What counts as Live's label for a session clip slot.
///
/// The rule exists because of a measured miss: a clip named "clip" was looked for with a
/// substring search, the search answered with the browser's "Clips" category - which sits earlier
/// in Live's tree - and the click selected nothing. Live then kept "Separate Stems to New Audio
/// Tracks" greyed out and the batch blamed the clip (run-20261115-191232, job 2).
/// </summary>
public class ClipSlotNamingTests
{
    [Theory]
    [InlineData("clip", "clip, stopped, in track 1-Audio, scene 1")]
    [InlineData("clip", "clip, playing, in track 1-Audio, scene 1")]
    [InlineData("clip", "Clip, stopped, in track 1-Audio, scene 1")]
    [InlineData("Akcept - Howl (Original Mix)", "Akcept - Howl (Original Mix), stopped, in track 1-Audio, scene 1")]
    [InlineData("clip", "clip")]
    public void LiveClipSlotLabelsAreAccepted(string clipName, string uiName)
    {
        Assert.True(LiveUi.IsClipSlotName(uiName, clipName));
    }

    [Theory]
    [InlineData("clip", "Clips")]
    [InlineData("clip", "Audio Clip")]
    [InlineData("clip", "clip2, stopped, in track 1-Audio, scene 1")]
    [InlineData("clip", "MIDI Clip, stopped, in track 1-MIDI, scene 1")]
    [InlineData("clip", "clip, in the browser")]
    [InlineData("clip", "")]
    [InlineData("", "clip, stopped, in track 1-Audio, scene 1")]
    public void AnythingElseIsNotTheSlot(string clipName, string uiName)
    {
        Assert.False(LiveUi.IsClipSlotName(uiName, clipName));
    }

    /// <summary>
    /// The guard against the fix being undone by a helper: the substring search that caused the
    /// miss does match "Clips", so the slot matcher must not be swapped back onto it.
    /// </summary>
    [Fact]
    public void TheSubstringSearchThatCausedTheMissStillMatchesTheBrowser()
    {
        Assert.Contains("clips", "Clips".ToLowerInvariant());
        Assert.False(LiveUi.IsClipSlotName("Clips", "clip"));
    }
}

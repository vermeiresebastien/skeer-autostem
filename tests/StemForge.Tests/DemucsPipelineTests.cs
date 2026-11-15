using StemForge.Core.Local;
using StemForge.Core.Models;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The arithmetic around a Demucs model, checked against what demucs 4.1.0 itself does. These
/// tests exist before any inference code because this is the part that corrupts audio silently:
/// a wrong stride, a wrong fade or a wrong pad is not an exception, it is a stem with a seam in it.
/// </summary>
public sealed class DemucsPipelineTests
{
    private const int ReferenceTrackSamples = 12_932_325;   // 293.25 s at 44.1 kHz - the Phase 0 source

    [Fact]
    public void HtdemucsCutsTenSecondWindowsSevenAndAHalfSecondsApart()
    {
        var plan = DemucsPlan.Htdemucs;

        Assert.Equal(44_100, plan.SampleRate);
        Assert.Equal(441_000, plan.SegmentLength);
        Assert.Equal(330_750, plan.Stride);
    }

    [Fact]
    public void TheLastWindowOfThePhase0SourceIsAShortOneInTheMiddleOfAWindow()
    {
        var windows = DemucsPlan.Htdemucs.Windows(ReferenceTrackSamples);

        Assert.Equal(40, windows.Count);
        Assert.Equal(0, windows[0].Offset);
        Assert.Equal(330_750, windows[1].Offset);

        var last = windows[^1];
        Assert.Equal(12_899_250, last.Offset);
        Assert.Equal(33_075, last.ChunkLength);
        // delta = 441000 - 33075 = 407925, and the reference pads half of it in front.
        Assert.Equal(203_962, last.PadLeft);
        Assert.Equal(12_695_288, last.WindowStart);
        Assert.Equal(ReferenceTrackSamples, last.ReadEnd(ReferenceTrackSamples));
    }

    [Fact]
    public void OnlyTheWindowsClosestToTheEndAreShort()
    {
        var plan = DemucsPlan.Htdemucs;
        var windows = plan.Windows(ReferenceTrackSamples);

        var shortOnes = 0;

        for (var i = 0; i < windows.Count; i++)
        {
            var window = windows[i];
            Assert.Equal(Math.Min(plan.SegmentLength, ReferenceTrackSamples - window.Offset), window.ChunkLength);
            Assert.Equal((plan.SegmentLength - window.ChunkLength) / 2, window.PadLeft);

            if (window.ChunkLength < plan.SegmentLength) shortOnes++;
        }

        // 293.25 s does not divide into 10 s windows that overlap by a quarter, so the tail is two
        // short windows, not one. The accumulator has to survive both.
        Assert.Equal(2, shortOnes);
        Assert.Equal(363_825, windows[^2].ChunkLength);
        Assert.Equal(38_587, windows[^2].PadLeft);
    }

    [Fact]
    public void TheFadeIsTheReferenceTriangle()
    {
        var fade = DemucsPlan.Htdemucs.FadeWeight();
        const int half = 441_000 / 2;

        Assert.Equal(441_000, fade.Length);
        Assert.Equal(1.0, fade[0] * half, 5);
        Assert.Equal(1f, fade[half - 1]);
        Assert.Equal(1f, fade[half]);
        Assert.Equal(1.0, fade[^1] * half, 5);

        for (var i = 1; i < half; i++) Assert.True(fade[i] > fade[i - 1]);
        for (var i = half + 1; i < fade.Length; i++) Assert.True(fade[i] < fade[i - 1]);
    }

    [Fact]
    public void ATrackShorterThanASegmentIsOneWindowCentredInSilence()
    {
        var plan = new DemucsPlan(1000, 1.0, 0.25);

        var windows = plan.Windows(300);

        Assert.Single(windows);
        Assert.Equal(0, windows[0].Offset);
        Assert.Equal(300, windows[0].ChunkLength);
        Assert.Equal(-350, windows[0].WindowStart);
        Assert.Equal(0, windows[0].ReadStart);
    }

    [Fact]
    public void AnEngineThatReturnsWhatItWasGivenHandsBackTheOriginalMix()
    {
        // The geometry test: overlap-add of the fade, divided by the accumulated fade, must be an
        // identity. If the stride, the padding or the trim is wrong, the reconstructed mix shows
        // it as a step, a gap or a level change - all of which are silent in the unit below.
        var plan = new DemucsPlan(1000, 1.0, 0.25);
        const int total = 3000;
        const int channels = 2;

        var left = new float[total];
        var right = new float[total];
        for (var i = 0; i < total; i++)
        {
            left[i] = MathF.Sin(i / 7.3f);
            right[i] = MathF.Cos(i / 11.1f) * 0.5f;
        }

        var stems = new[] { Stems.Drums, Stems.Bass, Stems.Others, Stems.Vocals };
        var accumulator = new DemucsOverlapAccumulator(plan, total, stems, channels);

        foreach (var window in plan.Windows(total))
        {
            var segment = new float[stems.Length * channels * plan.SegmentLength];

            for (var stem = 0; stem < stems.Length; stem++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var slice = (stem * channels + channel) * plan.SegmentLength;
                    var source = channel == 0 ? left : right;

                    for (var i = 0; i < plan.SegmentLength; i++)
                    {
                        var sample = window.WindowStart + i;
                        segment[slice + i] = sample >= 0 && sample < total ? source[sample] : 0f;
                    }
                }
            }

            accumulator.Add(window, segment);
        }

        var rebuilt = accumulator.Complete(0);

        for (var i = 0; i < total; i++)
        {
            Assert.True(accumulator.WeightAt(i) > 0, $"sample {i} was never covered");
            AssertNear(left[i], rebuilt[i * channels], 1e-6f, $"sample {i} left");
            AssertNear(right[i], rebuilt[i * channels + 1], 1e-6f, $"sample {i} right");
        }
    }

    /// <summary>
    /// Absolute tolerance rather than decimal places: overlap-add and a scale round trip are
    /// exact in real arithmetic and only float32-close in this one, and rounding to a number of
    /// decimals turns a 6e-8 difference into a failure whenever a value sits on a boundary.
    /// </summary>
    private static void AssertNear(float expected, float actual, float tolerance, string what)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"{what}: expected {expected}, got {actual} (off by {Math.Abs(expected - actual):E3})");
    }

    [Fact]
    public void ATailWindowIsAccumulatedWithTheFadeItActuallyReaches()
    {
        var plan = new DemucsPlan(1000, 1.0, 0.25);
        const int total = 1300;

        var stems = new[] { Stems.Vocals };
        var accumulator = new DemucsOverlapAccumulator(plan, total, stems, 1);
        var windows = plan.Windows(total);

        Assert.Equal(2, windows.Count);
        Assert.Equal(550, windows[1].ChunkLength);

        foreach (var window in windows)
        {
            var segment = new float[plan.SegmentLength];
            for (var i = 0; i < plan.SegmentLength; i++)
            {
                var sample = window.WindowStart + i;
                segment[i] = sample >= 0 && sample < total ? 1f : 0f;
            }

            accumulator.Add(window, segment);
        }

        var rebuilt = accumulator.Complete(0);

        for (var i = 0; i < total; i++) AssertNear(1f, rebuilt[i], 1e-6f, $"sample {i}");
    }

    [Fact]
    public void AWindowShorterThanTheSegmentIsRejected()
    {
        var plan = new DemucsPlan(1000, 1.0, 0.25);
        var accumulator = new DemucsOverlapAccumulator(plan, 1500, [Stems.Vocals], 1);
        var window = plan.Windows(1500)[0];

        Assert.Throws<ArgumentException>(
            () => accumulator.Add(window, new float[plan.SegmentLength - 1]));
    }

    [Fact]
    public void LevelFollowsDemucsAndSurvivesARoundTrip()
    {
        var left = new float[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
        var right = new float[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };

        var level = DemucsLevel.FromStereo(left, right);

        Assert.Equal(0.5f, level.Mean, 5);
        // mono reference is the channel mean, so std is the unbiased one over that reference.
        var mono = left.Select((v, i) => 0.5f * (v + right[i])).ToArray();
        var mean = mono.Average();
        var unbiased = Math.Sqrt(mono.Sum(v => (v - mean) * (v - mean)) / (mono.Length - 1));
        AssertNear((float)unbiased + 1e-8f, level.Std, 1e-7f, "the unbiased std");

        var biased = Math.Sqrt(mono.Sum(v => (v - mean) * (v - mean)) / mono.Length);
        Assert.True(Math.Abs(level.Std - (float)biased) > 1e-4f, "demucs uses Python's default, which is N-1");

        var scaled = (float[])left.Clone();
        level.Apply(scaled);
        Assert.Equal(0f, scaled.Average(), 5);
        level.Undo(scaled);

        for (var i = 0; i < left.Length; i++) AssertNear(left[i], scaled[i], 1e-6f, $"sample {i}");
    }

    [Fact]
    public void DemucsDoesNotReturnStemsInAbletonsOrder()
    {
        Assert.Equal(Stems.Drums, DemucsStemOrder.StemAt(0));
        Assert.Equal(Stems.Bass, DemucsStemOrder.StemAt(1));
        Assert.Equal(Stems.Others, DemucsStemOrder.StemAt(2));
        Assert.Equal(Stems.Vocals, DemucsStemOrder.StemAt(3));

        // The guard against someone "tidying" this into the Live order: index 0 and Live's index 0
        // are different stems, which is exactly the swap that would ship wrong-labelled files.
        Assert.NotEqual(StemsExtensions.Order[0], DemucsStemOrder.FourSources[0]);

        Assert.Throws<ArgumentOutOfRangeException>(() => DemucsStemOrder.StemAt(4));
    }

    [Fact]
    public void OnlyTheRequestedStemsAreTakenOutOfTheModelOutput()
    {
        Assert.Equal(new[] { 1, 3 }, DemucsStemOrder.IndicesFor(Stems.Bass | Stems.Vocals));
        Assert.Equal(new[] { 0, 1, 2, 3 }, DemucsStemOrder.IndicesFor(Stems.All));
        Assert.Empty(DemucsStemOrder.IndicesFor(Stems.None));
    }
}

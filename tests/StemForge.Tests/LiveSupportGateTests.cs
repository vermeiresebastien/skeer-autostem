using System.Text.RegularExpressions;
using StemForge.Core.Engines;
using StemForge.Core.Live;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// Which Ableton Live this product will drive, and what it says when it will not.
///
/// The gate has two halves that fail differently: a build below the floor has no "Separate Stems"
/// command at all, and a new enough build in an edition without it has that command sitting greyed
/// out in the Create menu - which is a thing the user only finds out about from inside Live, after a
/// run has taken their session over. So the edition half is asked here, before anything is touched.
///
/// Nothing in this file is evidence about a real Ableton Live. It is the table of decisions, the
/// sentences that carry them, and the wiring that keeps a copy of the floor from drifting away from
/// the constant it came from.
/// </summary>
public sealed class LiveSupportGateTests
{
    /// <summary>The floor as prose, read off the constant every sentence is supposed to use.</summary>
    private static string Floor =>
        $"{LiveInstall.MinimumStemSeparationVersion.Major}.{LiveInstall.MinimumStemSeparationVersion.Minor}";

    [Theory]
    // At the floor and newer, in Suite or in an edition this tool could not name: driven.
    [InlineData("12.3", "Suite", true, LiveInstall.SupportedReason)]
    [InlineData("12.3.2", "Suite", true, LiveInstall.SupportedReason)]
    [InlineData("12.3.5", "Suite", true, LiveInstall.SupportedReason)]
    [InlineData("12.4.16", "Suite", true, LiveInstall.SupportedReason)]
    [InlineData("13.0.1", "Suite", true, LiveInstall.SupportedReason)]
    [InlineData("12.3.2", "Live", true, LiveInstall.SupportedReason)]
    [InlineData("12.3.2", "", true, LiveInstall.SupportedReason)]
    [InlineData("12.3.2", null, true, LiveInstall.SupportedReason)]
    // New enough, wrong box: refused by edition.
    [InlineData("12.3", "Standard", false, LiveInstall.EditionUnsupportedReason)]
    [InlineData("12.3.2", "Standard", false, LiveInstall.EditionUnsupportedReason)]
    [InlineData("12.4.16", "Intro", false, LiveInstall.EditionUnsupportedReason)]
    [InlineData("12.3.2", "Lite", false, LiveInstall.EditionUnsupportedReason)]
    // Too old: refused by version, which is asked first even when the edition is also wrong.
    [InlineData("12.1", "Suite", false, LiveInstall.VersionUnsupportedReason)]
    [InlineData("12.2.20", "Suite", false, LiveInstall.VersionUnsupportedReason)]
    [InlineData("11.3.8", "Suite", false, LiveInstall.VersionUnsupportedReason)]
    [InlineData("10", "Suite", false, LiveInstall.VersionUnsupportedReason)]
    [InlineData("12.1", "Standard", false, LiveInstall.VersionUnsupportedReason)]
    // Nothing readable to compare against the floor.
    [InlineData("", "Suite", false, LiveInstall.VersionUnsupportedReason)]
    [InlineData("   ", "Suite", false, LiveInstall.VersionUnsupportedReason)]
    [InlineData("12.3 beta", "Suite", false, LiveInstall.VersionUnsupportedReason)]
    [InlineData(null, "Suite", false, LiveInstall.VersionUnsupportedReason)]
    // The test double is not an installation and there is no Ableton to check.
    [InlineData("0.0", "Simulated", true, LiveInstall.SupportedReason)]
    public void TheGateAnswersTheWholeTable(string? version, string? edition, bool supported, string reason)
    {
        var verdict = LiveInstall.CheckSupport(version, edition);

        Assert.Equal(supported, verdict.IsSupported);
        Assert.Equal(reason, verdict.Reason);
    }

    [Fact]
    public void TheFloorIsTheReleaseThatIntroducedTheCommandAndTheEditionThatCarriesIt()
    {
        // The evidence is docs/LICENSING-REQUIREMENTS.md §1. The 12.1 that used to be here was a doc
        // comment restating another doc comment, and the edition was never compared at all. This is
        // the pin that makes reverting either half loud.
        Assert.Equal(new Version(12, 3), LiveInstall.MinimumStemSeparationVersion);
        Assert.Equal("Suite", LiveInstall.RequiredEdition);

        Assert.Contains("Standard", LiveInstall.EditionsWithoutStemSeparation);
        Assert.Contains("Intro", LiveInstall.EditionsWithoutStemSeparation);
        Assert.Contains("Lite", LiveInstall.EditionsWithoutStemSeparation);
        Assert.DoesNotContain("Suite", LiveInstall.EditionsWithoutStemSeparation);
    }

    [Fact]
    public void TheRequirementSentenceIsBuiltFromTheTwoConstants()
    {
        Assert.Equal($"Live {Floor} {LiveInstall.RequiredEdition} or newer", LiveInstall.RequirementPhrase);
        Assert.Equal($"{LiveInstall.RequirementPhrase} is required.", LiveInstall.RequirementSummary);
    }

    [Theory]
    [InlineData("12.1", "Suite")]
    [InlineData("12.3.2", "Standard")]
    [InlineData("", "Suite")]
    [InlineData("12.3 beta", "Suite")]
    public void EveryRefusalEndsWithTheRequirementReadOffTheConstant(string version, string edition)
    {
        var verdict = LiveInstall.CheckSupport(version, edition);

        Assert.False(verdict.IsSupported);
        Assert.EndsWith(LiveInstall.RequirementSummary, verdict.Sentence);

        // The token travels with the sentence into the failure row, so a report can be classified
        // without opening the code.
        Assert.Equal($"{verdict.Sentence} ({verdict.Reason})", verdict.FailureText);
    }

    [Fact]
    public void TheVersionRefusalNamesTheBuildItSaw()
    {
        var verdict = LiveInstall.CheckSupport("12.1", "Suite");

        Assert.Equal($"Ableton Live 12.1 has no stem separation. {LiveInstall.RequirementSummary}",
            verdict.Sentence);
    }

    [Fact]
    public void TheEditionRefusalNamesTheEditionItSaw()
    {
        // The whole point of this half: a Standard user used to be told they were covered.
        var verdict = LiveInstall.CheckSupport("12.3.2", "Standard");

        Assert.Equal($"Ableton Live 12.3.2 Standard cannot separate stems. {LiveInstall.RequirementSummary}",
            verdict.Sentence);
        Assert.Equal(LiveInstall.EditionUnsupportedReason, verdict.Reason);
    }

    [Fact]
    public void AVersionThatCouldNotBeReadSaysSoInsteadOfBlamingABuildItNeverSaw()
    {
        Assert.Equal($"No Ableton Live version was detected. {LiveInstall.RequirementSummary}",
            LiveInstall.CheckSupport(null, "Suite").Sentence);

        Assert.Equal($"\"12.3 beta\" is not an Ableton Live version this tool can read. " +
                     $"{LiveInstall.RequirementSummary}",
            LiveInstall.CheckSupport("12.3 beta", "Suite").Sentence);
    }

    [Fact]
    public void ASupportedVerdictHasNothingToSay()
    {
        var verdict = LiveInstall.CheckSupport("12.3.2", "Suite");

        Assert.Equal(string.Empty, verdict.Sentence);
        Assert.Equal(string.Empty, verdict.FailureText);
    }

    [Theory]
    [InlineData("Live")]
    [InlineData("Suite")]
    [InlineData("Standard Plus")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void AnEditionThisToolCannotNameIsNotRefused(string? edition)
    {
        // Deliberate, and reversible only in this direction: refusing an unrecognised string would
        // lock a real Suite install out of its own batch because a product name did not match. The
        // cost is a fooled detector meeting a greyed-out menu, which is the failure the gate exists
        // to prevent but is not made worse by a short list.
        Assert.True(LiveInstall.CheckSupport("12.3.2", edition).IsSupported);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData(" STANDARD ")]
    [InlineData("Intro")]
    [InlineData("lite")]
    public void TheEditionListIsMatchedWhateverShapeItArrivesIn(string edition) =>
        Assert.Equal(LiveInstall.EditionUnsupportedReason,
            LiveInstall.CheckSupport("12.3.2", edition).Reason);

    [Fact]
    public void TheReasonTokensAreTheOnesTheLogsAreGreppedFor()
    {
        // Literal here on purpose: these strings are an external contract (RunLog, failure rows),
        // so a rename has to be a deliberate act that reads as one.
        Assert.Equal("live-supported", LiveInstall.SupportedReason);
        Assert.Equal("live-version-unsupported", LiveInstall.VersionUnsupportedReason);
        Assert.Equal("live-edition-unsupported", LiveInstall.EditionUnsupportedReason);

        Assert.Equal(3, new[]
        {
            LiveInstall.SupportedReason,
            LiveInstall.VersionUnsupportedReason,
            LiveInstall.EditionUnsupportedReason,
        }.Distinct().Count());
    }

    [Fact]
    public void AnInstallAnswersWithTheSameVerdictTheGateGives()
    {
        var suite = new LiveInstall(@"C:\A\Live 12 Suite\Program\Live.exe", "12.3.2", "Suite");
        var standard = new LiveInstall(@"C:\A\Live 12 Standard\Program\Live.exe", "12.3.2", "Standard");

        Assert.True(suite.IsSupported);
        Assert.True(suite.SupportsStemSeparation);
        Assert.True(suite.EditionIsSupported);
        Assert.True(suite.SupportsBothProcessingModes);

        // The trap that produced this change, kept visible: the version half is true for a build
        // that cannot separate a stem. Only Support is the answer to "can this be driven".
        Assert.True(standard.SupportsStemSeparation);
        Assert.False(standard.EditionIsSupported);
        Assert.False(standard.IsSupported);

        // Neither processing mode exists without the dialog that exposes them.
        Assert.False(standard.SupportsBothProcessingModes);
    }

    [Fact]
    public void TheEngineListQuotesTheFloorRatherThanRestatingIt()
    {
        // This row is shown in the UI, so a version number written here would be a second,
        // untested answer to the same question.
        var live = SeparatorFactory.Catalogue.Single(info => info.Kind == EngineKind.Live);

        Assert.Contains(LiveInstall.RequirementPhrase, live.Description);
        AssertAllDottedNumbersAreTheFloor(live.Description);
    }

    private static void AssertAllDottedNumbersAreTheFloor(string text)
    {
        // How the stale 12.1 came back into this product: one copy per prose surface, and only one
        // of them was the source of truth. Any other number in a requirement-stating string is a
        // copy, so it has to go through the constant like everything else.
        foreach (var match in Regex.Matches(text, @"\d+(?:\.\d+)+").Cast<Match>())
            Assert.Equal(Floor, match.Value);
    }
}

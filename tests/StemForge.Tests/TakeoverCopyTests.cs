using System.Collections.Generic;
using StemForge.Core.Live;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The sentences a run says before it touches Ableton Live.
///
/// They are asserted on the string the view model actually composes - it calls
/// <see cref="LiveRunCopy"/> and shows what comes back - so a change to the wording a user reads
/// changes what a check reads. The rule being gated is one-directional and easy to write backwards
/// by accident: a run that cannot save an open set refuses, and the set stays exactly where it is.
/// A sentence saying the unsalvable save is "dropped" is not a stylistic complaint, it is the
/// product telling the user it may eat work it has promised not to eat.
///
/// Both directions are asserted, because an absence-only check stays green when the sentence is
/// deleted rather than rewritten.
/// </summary>
public sealed class TakeoverCopyTests
{
    /// <summary>
    /// Promises that the open set's work does not survive. Deliberately phrased as verb-plus-object
    /// rather than as the bare word "dropped": the correct sentence says a set that cannot be saved
    /// "is not thrown away either", and a check that banned that word outright would have to ban the
    /// fix.
    /// </summary>
    private static readonly string[] ForbiddenPromisePhrases =
    [
        "is dropped",
        "are dropped",
        "dropped rather than",
        "will be dropped",
        "gets dropped",
        "is discarded",
        "are discarded",
        "will be discarded",
        "discarded rather than",
        "thrown away rather than",
        "closed without saving",
    ];

    [Fact]
    public void TakeoverCopyNeverPromisesToDiscardTheOpenSet()
    {
        var open = new List<RunningLiveSession>
        {
            new(4321, "Untitled.als", LiveSessionKind.Foreign),
            new(5678, "Night mix.als", LiveSessionKind.Foreign),
        };

        var confirmation = LiveRunCopy.TakeoverConfirmation(open, @"C:\Skeer\recovered");
        var conditions = LiveRunCopy.RunConditions("Ableton Live 12.3 Suite");
        var declined = LiveRunCopy.TakeoverDeclined();

        // Absent: no carrier of the promise, in any of the three. Checked first on purpose - the
        // two mutants this gate was measured against redden on different halves, and a first
        // failing assertion hides the ones under it, so an order that ran the presence checks
        // first would leave the forbidden-word list never observed failing.
        foreach (var phrase in ForbiddenPromisePhrases)
        {
            Assert.DoesNotContain(phrase, confirmation);
            Assert.DoesNotContain(phrase, conditions);
            Assert.DoesNotContain(phrase, declined);
        }

        // Present: the refusal, and the open set being left where it is. This half is what stops
        // the gate going green because somebody deleted the sentence instead of rewriting it.
        Assert.Contains("it is left open", confirmation);
        Assert.Contains("the run stops before anything is closed", confirmation);
        Assert.Contains("the log names the session it stopped on", confirmation);
        Assert.Contains("nothing of yours is replaced", confirmation);

        Assert.Contains("never discards an open set", conditions);
        Assert.Contains("stops before closing anything", conditions);
        Assert.Contains("names the session it left open", conditions);

        Assert.Contains("left alone", declined);
    }

    /// <summary>
    /// The list and the recovery folder have to survive the move into a builder: the user is shown
    /// what is about to be closed, by title and pid, and told where an unsaved set would land.
    /// Also the anti-vacuity half of the gate above - a method returning an empty string satisfies
    /// every DoesNotContain in it.
    /// </summary>
    [Fact]
    public void TakeoverCopyNamesEveryOpenSessionAndTheRecoveryFolder()
    {
        var open = new List<RunningLiveSession>
        {
            new(4321, "Untitled.als", LiveSessionKind.Foreign),
            new(7, "", LiveSessionKind.Starting),
        };

        var confirmation = LiveRunCopy.TakeoverConfirmation(open, @"C:\Skeer\recovered");

        Assert.Contains("\"Untitled.als\" (pid 4321)", confirmation);
        Assert.Contains("a Live window that is still opening (pid 7)", confirmation);
        Assert.Contains(@"  C:\Skeer\recovered", confirmation);

        var conditions = LiveRunCopy.RunConditions("Ableton Live 12.3 Suite");
        Assert.Contains("Ableton Live 12.3 Suite", conditions);
        Assert.Contains(LiveInstall.RequirementPhrase, conditions);

        Assert.StartsWith("user confirmed takeover of 2 open Live session(s)",
            LiveRunCopy.TakeoverConfirmed(2));
    }
}

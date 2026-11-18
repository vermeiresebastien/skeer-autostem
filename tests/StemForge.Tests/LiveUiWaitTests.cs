using System.Diagnostics;
using StemForge.Core.Live.Automation;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// The polling rule every UI Automation wait in the product runs through.
///
/// A wait is the part of a run that lasts, and it is where a cancelled batch used to keep working:
/// the deadline was only consulted between a look and a sleep, and the sleep was the whole poll
/// interval - so a five-second popup wait asked for one more look after it was over, and a wait
/// poll of fifteen hundred milliseconds kept a cancelled run busy for that long on every round. A
/// UIA read cannot be interrupted once it has started, so the only moment any of this can be
/// honoured is before the next one, which is what these cases pin.
///
/// Nothing here touches a real UI: <see cref="LiveUi.WaitFor"/> takes the lookup as a delegate, so
/// the three things it has to get right - the clock, the token and the sleep - can be measured
/// against a function this file writes.
/// </summary>
public sealed class LiveUiWaitTests
{
    [Fact]
    public void AWaitDoesNotLookAgainAfterItsDeadlineHasPassed()
    {
        var looks = 0;
        var started = Stopwatch.StartNew();

        var found = LiveUi.WaitFor(
            () => { looks++; return null; },
            TimeSpan.FromMilliseconds(250),
            pollMilliseconds: 5_000);

        started.Stop();

        Assert.Null(found);
        Assert.True(looks >= 1, "a wait that never looks is not waiting for anything");

        // Five seconds of poll, a quarter second of patience: the sleep has to be cut to what is
        // left, not taken in full after the deadline. Two seconds is generous for a 250 ms wait -
        // and a long way under the 5.25 s the same code takes when the clamp is missing.
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2),
            $"the wait slept past its deadline: {started.Elapsed.TotalMilliseconds:F0} ms for 250 ms of timeout");
    }

    [Fact]
    public void AWaitGivesUpBetweenLooksWhenTheBatchIsCancelled()
    {
        var looks = 0;
        using var cancel = new CancellationTokenSource();
        var started = Stopwatch.StartNew();

        var thrown = Record.Exception(() => LiveUi.WaitFor(
            () =>
            {
                looks++;

                // Cancelled while a look is in progress, which is the only moment there is: nothing
                // else is running, and the next thing this wait does is decide whether to look again.
                if (looks == 2) cancel.Cancel();
                return null;
            },
            TimeSpan.FromSeconds(10),
            pollMilliseconds: 50,
            cancellationToken: cancel.Token));

        started.Stop();

        // Cancellation surfaces as a cancellation, not as "nothing was found".
        Assert.IsType<OperationCanceledException>(thrown);

        // Ten seconds of patience, fifty milliseconds of poll: the cancellation is seen on the round
        // after it happens, and no round after that.
        Assert.InRange(looks, 2, 4);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2),
            $"the wait ran {started.Elapsed.TotalMilliseconds:F0} ms after cancellation");
    }

    [Fact]
    public void ACancellationFromTheLookupItselfIsNotTreatedAsATransientFailure()
    {
        // Everything else a UIA read throws is expected while a menu is being built, and the wait
        // retries it. A cancellation is not a transient failure: it is the batch saying stop, and
        // the catch-all clause that retries "any exception" used to retry that one too.
        var looks = 0;

        Assert.Throws<OperationCanceledException>(() => LiveUi.WaitFor(
            () =>
            {
                looks++;
                throw new OperationCanceledException();
            },
            TimeSpan.FromSeconds(10),
            pollMilliseconds: 25));

        Assert.Equal(1, looks);
    }

    [Fact]
    public void ATransientUiFailureIsRetriedUntilTheElementAppears()
    {
        // The other side of the same clause: Live's tree throws while it is building, and a wait that
        // believed the first throw would give up on a menu that was about to open.
        var looks = 0;

        var found = LiveUi.WaitFor(
            () =>
            {
                looks++;
                if (looks < 3) throw new InvalidOperationException("the tree is being rebuilt");
                return null;
            },
            TimeSpan.FromMilliseconds(500),
            pollMilliseconds: 10);

        Assert.Null(found);
        Assert.True(looks >= 3, $"the wait stopped at the first transient failure after {looks} look(s)");
    }
}

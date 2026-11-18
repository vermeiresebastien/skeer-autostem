using StemForge.Core.Live.Automation;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// Which key answers which of Live's dialogs, and when no key may be claimed to.
///
/// The reward for getting this right is the whole point of the product: a posted key reaches the
/// dialog's own queue, so Live's startup question can be answered with the cursor where it was and
/// the screen in the user's hands. Measured against the machine this runs on, the recovery question
/// offered Close|Yes|No and a posted VK 78 - 'N' - dismissed it and Live carried on loading its
/// control surfaces. The cost of getting it wrong is pressing a button on a dialog that decides
/// whether an unsaved set survives, so the ambiguous cases are refused here rather than guessed at.
/// </summary>
public sealed class DialogKeyTests
{
    /// <summary>The dialog Live actually showed, and the key that was proven to answer it.</summary>
    [Fact]
    public void TheMeasuredRecoveryQuestionIsAnsweredByItsFirstLetter()
    {
        var dialog = new[] { "Close", "Yes", "No" };

        // 78 is what the probe posted; a VK for a letter is that letter uppercased.
        Assert.Equal((ushort)0x4E, DialogKey.ForLabel("No", dialog));
    }

    [Theory]
    [InlineData("No", "Close|Yes|No", 0x4E)]
    [InlineData("Yes", "Close|Yes|No", 0x59)]
    [InlineData("Don't Recover", "Close|Don't Recover|Recover", 0x44)]
    [InlineData("Don’t Save", "Save|Don’t Save", 0x44)]
    [InlineData("Dont Save", "Dont Save|Save", 0x44)]
    [InlineData("OK", "Close|OK|Cancel", 0x4F)]
    [InlineData("no", "Yes|no", 0x4E)]
    public void AButtonWhoseFirstLetterIsUniqueInTheDialogIsReachedByKey(string label, string tree, ushort expected)
    {
        var found = DialogKey.ForLabel(label, tree.Split('|'));

        Assert.Equal(expected, found);
    }

    [Theory]
    [InlineData("Cancel", "Close|Cancel")]
    [InlineData("Close", "Close|Cancel")]
    [InlineData("Save", "Save|Save to Recovery|Don't Save")]
    [InlineData("Recover", "Recover|Recover later")]
    public void TwoButtonsSharingAFirstLetterAreNotAGuess(string label, string tree)
    {
        Assert.Null(DialogKey.ForLabel(label, tree.Split('|')));
    }

    /// <summary>
    /// The anti-shortcut guard: counting only the *other* buttons would call this safe, and then
    /// the run would post a key at a dialog where two buttons answer to it and Live chooses.
    /// </summary>
    [Fact]
    public void TheSameLabelTwiceIsTheAmbiguityItIs()
    {
        Assert.Null(DialogKey.ForLabel("No", new[] { "No", "No" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("…")]
    [InlineData("1")]
    [InlineData("Äcker")]
    public void ALabelWithoutAPlainLatinFirstLetterHasNoKey(string label)
    {
        // A VK is a physical key. There is no code that stands in for a character Live's button
        // does not carry, and "Ä" is not "A" as far as a message queue is concerned.
        Assert.Null(DialogKey.ForLabel(label, new[] { label, "No" }));
    }

    [Fact]
    public void ThePunctuationInFrontOfTheLetterIsNotTheKey()
    {
        Assert.Equal((ushort)0x4E, DialogKey.ForLabel("'No'", new[] { "Yes", "'No'" }));
    }

    /// <summary>
    /// The handle is one this test owned and let go, not a number picked out of the air.
    ///
    /// A caller runs this against a window it found a moment ago, which a closing Live may have
    /// destroyed in the meantime, and a thrown P/Invoke there would abort a run that had already
    /// finished its work. A made-up value proves nothing about that: it is refused only because it
    /// happens not to name a window, and the suite cannot tell that apart from a message that went
    /// somewhere. A real handle whose window is gone is the case the product is in - and the check
    /// that answers it is <c>IsWindow</c>, asked before anything is queued.
    /// </summary>
    [Fact]
    public void PostingToAWindowThatHasGoneIsRefusedRatherThanThrown()
    {
        var injectionsBefore = InputSimulator.InjectionAttempts;
        IntPtr handle;

        using (var window = new FakeFormHost(
                   () => new FakeSaveDialog(FakeSaveDialog.Confirm.None)))
        {
            handle = window.Handle;
            Assert.True(WindowControl.IsVisible(handle), "the fake dialog never appeared");
        }

        Assert.False(WindowControl.IsVisible(handle), "the dialog outlived the host that closed it");
        Assert.False(InputSimulator.PostKey(handle, DialogKeyVk), "a destroyed window accepted a key");

        // The two shapes of "no window here": never was one, and was one until a moment ago.
        Assert.False(InputSimulator.PostKey(IntPtr.Zero, DialogKeyVk));

        // A posted key is a message to one queue, not synthetic input: none of the above may reach
        // SendInput, and this is the measurement that says it did not.
        Assert.Equal(injectionsBefore, InputSimulator.InjectionAttempts);
    }

    /// <summary>VK 78 - the 'N' Live's recovery question was measured answering.</summary>
    private const ushort DialogKeyVk = 0x4E;
}

using System.Windows.Forms;
using StemForge.Core.Live.Automation;
using Xunit;

namespace StemForge.Tests;

/// <summary>
/// What <see cref="InputSimulator.PostKey"/> actually does, measured on a window this test makes
/// for itself: the message arrives at that window's queue, as WM_KEYDOWN then WM_KEYUP with the
/// virtual key in wParam, and the posted-key path never reaches <see cref="InputSimulator.Send"/>.
///
/// The second half is asserted by construction rather than by sampling the desktop. An earlier
/// version of this test read the cursor position before and after and called a mismatch a bug;
/// that measurement is racy on a machine somebody is using - their pointer may move for reasons
/// of their own between the two reads - and it only notices input that lands where the pointer
/// was. Every injected event in this class goes through Send, which counts itself; a delta of zero
/// across the posted key is the stronger claim and needs nothing from the user's desktop.
///
/// Live's dialogs are the reason the product needs this, and the promise made to the user is
/// "no mouse, no screen". A real Live modal is still owed its own run; what this rules out is a
/// wiring mistake in the two message numbers, the parameter, or a path that quietly reaches for
/// SendInput.
/// </summary>
public sealed class PostedKeyTests
{
    private const ushort VkN = 0x4E;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;

    /// <summary>An off-screen, never-shown window that writes down the key messages it is sent.</summary>
    private sealed class KeyRecorder : Form
    {
        public List<(int Message, int Key)> Received { get; } = new();

        public KeyRecorder()
        {
            Visible = false;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(-32000, -32000);
            Size = new System.Drawing.Size(1, 1);
        }

        protected override bool ShowWithoutActivation => true;

        public void CreateWithoutShowing() => CreateHandle();

        protected override void WndProc(ref Message message)
        {
            if (message.Msg is WmKeyDown or WmKeyUp)
            {
                lock (Received) Received.Add((message.Msg, message.WParam.ToInt32()));
            }

            base.WndProc(ref message);
        }
    }

    [Fact]
    public void APostedKeyReachesTheWindowItWasAddressedToAndInjectsNothing()
    {
        var injectionsBefore = InputSimulator.InjectionAttempts;

        var posted = false;
        var postedWhileHandless = true;
        List<(int Message, int Key)> received = new();

        // WinForms wants an apartment and a message pump of its own; the pump is DoEvents, because
        // a modal loop would need a window the user can see.
        var thread = new Thread(() =>
        {
            using var window = new KeyRecorder();
            window.CreateWithoutShowing();

            postedWhileHandless = InputSimulator.PostKey(IntPtr.Zero, VkN);
            posted = InputSimulator.PostKey(window.Handle, VkN);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                lock (window.Received) received = new List<(int, int)>(window.Received);

                if (received.Count(message => message.Key == VkN) >= 2) break;
                Thread.Sleep(5);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "the recorder thread never finished");

        Assert.True(posted, "PostKey refused to post to a window that exists");
        Assert.False(postedWhileHandless, "PostKey must refuse a zero handle rather than post somewhere");
        Assert.Contains((WmKeyDown, (int)VkN), received);
        Assert.Contains((WmKeyUp, (int)VkN), received);

        // The whole point: the posted key is a message, not synthetic input. A delta of one here
        // means some path in PostKey reached SendInput, and the test does not need to know where
        // the user's pointer is to say so.
        Assert.Equal(injectionsBefore, InputSimulator.InjectionAttempts);
    }
}

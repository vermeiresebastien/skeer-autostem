using System.Runtime.InteropServices;
using System.Windows.Forms;
using StemForge.Core.Live;
using StemForge.Core.Live.Automation;

namespace StemForge.Tests;

/// <summary>
/// Windows that belong to the test process and stand in for Ableton's own: a main window with
/// whatever title a case needs, and a dialog shaped like Live's Save As.
///
/// Everything here is parked off the visible desktop and never activated. The point is that a
/// rule about somebody's unsaved set can be exercised without their Live, their screen or their
/// pointer - the suite asserting its own promises instead of taking them on trust.
/// </summary>
internal static class FakeWindows
{
    internal const int WmKeyDown = 0x0100;
    internal const int VkReturn = 0x0D;
    internal const int VkEscape = 0x1B;

    [DllImport("user32.dll")]
    internal static extern bool EnableWindow(IntPtr hWnd, bool enable);
}

/// <summary>
/// A dialog with the shape that matters: a labelled file-name field, an optional confirm button,
/// and its own answer to Enter. It records what it was asked to save, so a test can tell "named
/// and confirmed" from "dismissed".
/// </summary>
internal sealed class FakeSaveDialog : Form
{
    internal enum Confirm
    {
        /// <summary>The button writes the typed path, the way Live writes the set.</summary>
        WritesTheFile,

        /// <summary>The button is offered and enabled, and pressing it does nothing.</summary>
        Inert,

        /// <summary>The button is offered and disabled, so no pattern can press it.</summary>
        Disabled,

        /// <summary>No button the takeover would recognise.</summary>
        None,
    }

    private readonly TextBox? _name;
    private readonly Confirm _confirm;
    private readonly bool _enterConfirms;

    internal FakeSaveDialog(Confirm confirm, bool hasEdit = true, bool readOnlyField = false, bool enterConfirms = false)
    {
        _confirm = confirm;
        _enterConfirms = enterConfirms;

        Text = "Save Live Set As";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        Location = new System.Drawing.Point(-32000, -32000);
        ClientSize = new System.Drawing.Size(420, 140);
        ShowInTaskbar = false;

        if (hasEdit)
        {
            Controls.Add(new Label { Text = "File name:", Location = new System.Drawing.Point(12, 16), AutoSize = true });
            _name = new TextBox { Location = new System.Drawing.Point(12, 40), Width = 390, ReadOnly = readOnlyField };
            Controls.Add(_name);
        }

        if (confirm != Confirm.None)
        {
            var save = new Button
            {
                Text = "Save",
                Location = new System.Drawing.Point(240, 92),
                Width = 80,
                Enabled = confirm != Confirm.Disabled,
            };

            if (confirm == Confirm.WritesTheFile) save.Click += (_, _) => ConfirmAndClose();
            Controls.Add(save);
        }

        var cancel = new Button { Text = "Cancel", Location = new System.Drawing.Point(326, 92), Width = 80 };
        cancel.Click += (_, _) => { CancelPressed = true; Close(); };
        Controls.Add(cancel);
    }

    internal string? WrittenPath { get; private set; }

    internal bool CancelPressed { get; private set; }

    internal bool EscapePressed { get; private set; }

    protected override bool ShowWithoutActivation => true;

    private void ConfirmAndClose()
    {
        if (_name is null)
        {
            Close();
            return;
        }

        WrittenPath = _name.Text;
        try
        {
            File.WriteAllText(WrittenPath, "a fake .als written by the test dialog");
        }
        catch
        {
            // The assertion on the file is where a failure belongs, not here.
        }

        Close();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == FakeWindows.WmKeyDown)
        {
            var key = message.WParam.ToInt32();

            if (key == FakeWindows.VkEscape) EscapePressed = true;

            // A posted Enter is what a dialog without a recognised button has to answer. This
            // dialog's answer is the case's choice: confirm, or ignore it and stay up.
            if (key == FakeWindows.VkReturn)
            {
                if (_enterConfirms) ConfirmAndClose();
                return;
            }
        }

        base.WndProc(ref message);
    }
}

/// <summary>Runs one window on its own STA thread with a message pump, for as long as it is up.</summary>
internal sealed class FakeFormHost : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Form? _form;
    private IntPtr _handle;

    internal FakeFormHost(Func<Form> create)
    {
        _thread = new Thread(() =>
        {
            try
            {
                _form = create();
                _form.Show();

                // Captured once, while the window exists: reading Form.Handle after the form is
                // disposed throws, and cleanup has to be able to ask whether the window is gone.
                _handle = _form.Handle;
                _ready.Set();
                Application.Run(_form);
            }
            catch (Exception ex)
            {
                Failure = ex;
                _ready.Set();
            }
        })
        {
            IsBackground = true,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("the fake window never appeared");
        if (Failure is not null)
            throw new InvalidOperationException("the fake window failed to start", Failure);
    }

    internal Exception? Failure { get; private set; }

    internal Form Form => _form ?? throw new InvalidOperationException("the window was never created");

    internal IntPtr Handle => _handle;

    internal TopLevelWindow Window => new(Handle, Form.Text, "FakeWindow", true);

    /// <summary>Runs an action on the window's own thread, so WinForms state is never touched across threads.</summary>
    internal void OnUi(Action<Form> action)
    {
        var form = Form;
        if (form.IsDisposed) return;
        if (form.InvokeRequired) form.Invoke(() => action(form));
        else action(form);
    }

    public void Dispose()
    {
        try
        {
            if (_thread.IsAlive) Form.BeginInvoke(new Action(() => Form.Close()));
        }
        catch
        {
            // The loop already ended; there is nothing left to close.
        }

        _thread.Join(TimeSpan.FromSeconds(5));
    }
}

/// <summary>
/// A stand-in for one running Live: a titled main window plus the dialogs it raises, all in this
/// process. The takeover only ever reads windows and asks for actions, so it cannot tell the
/// difference - which is exactly what makes the refusal path testable.
/// </summary>
internal sealed class FakeLiveSession : IDisposable
{
    private readonly FakeFormHost _host;
    private readonly List<FakeSaveDialog> _dialogs = new();

    internal FakeLiveSession(string title)
    {
        _host = new FakeFormHost(() => new MainWindow { Text = title });
    }

    internal IntPtr MainHandle => _host.Handle;

    internal Form Main => _host.Form;

    /// <summary>
    /// Shows a dialog and, like a modal, makes the main window look blocked while it is up. The
    /// dialog is constructed on its own UI thread - a WinForms control belongs to the thread that
    /// creates its handle, and building it here only to show it there is the kind of cross-thread
    /// accident this fixture must not have.
    /// </summary>
    internal FakeSaveDialog RaiseDialog(Func<FakeSaveDialog> create, bool blockOwner = true)
    {
        FakeSaveDialog? dialog = null;

        _host.OnUi(main =>
        {
            if (blockOwner) FakeWindows.EnableWindow(main.Handle, false);
            dialog = create();
            _dialogs.Add(dialog);
            dialog.Show();
        });

        return dialog ?? throw new InvalidOperationException("the dialog was never created");
    }

    internal void SetTitle(string title) => _host.OnUi(main => main.Text = title);

    internal void CloseMain() => _host.OnUi(main =>
    {
        FakeWindows.EnableWindow(main.Handle, true);
        main.Close();
    });

    public void Dispose()
    {
        try
        {
            _host.OnUi(main =>
            {
                FakeWindows.EnableWindow(main.Handle, true);
                foreach (var dialog in _dialogs)
                    if (!dialog.IsDisposed) dialog.Close();
            });
        }
        catch
        {
            // The pump is gone; the windows go with the process.
        }

        _host.Dispose();

        // Windows belong to the process being reused by the next case: leave no visible, titled
        // window behind for its own window search to mistake for the session it just made.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && WindowControl.IsVisible(MainHandle))
            Thread.Sleep(20);
    }

    private sealed class MainWindow : Form
    {
        internal MainWindow()
        {
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(-32000, -32000);
            ClientSize = new System.Drawing.Size(600, 400);
            ShowInTaskbar = false;
        }

        protected override bool ShowWithoutActivation => true;
    }
}

/// <summary>
/// The takeover's process-level actions, recorded instead of performed. Nothing here sends input,
/// moves a pointer or ends a process: <see cref="Alive"/> is the fake session going away.
/// </summary>
internal sealed class FakeTakeoverActions : ITakeoverActions
{
    private volatile bool _alive = true;

    internal bool Alive
    {
        get => _alive;
        set => _alive = value;
    }

    internal Func<IntPtr, bool> Activate { get; set; } = _ => true;

    internal Action? OnSave { get; set; }

    /// <summary>
    /// What the fake says about the save keystroke. A refusal is a normal answer, so the rules above
    /// it have to be exercised against one without anybody's desktop being touched.
    /// </summary>
    internal InjectionResult SaveVerdict { get; set; } =
        new(InjectionOutcome.Injected, "injected", "the save keystroke was asked for");

    /// <summary>Which window the save was aimed at - the session's own, or nothing.</summary>
    internal IntPtr SaveTarget { get; private set; }

    internal Action? OnClose { get; set; }

    internal int Saves { get; private set; }

    internal int Closes { get; private set; }

    internal int Terminations { get; private set; }

    public bool TryActivateMain(IntPtr main) => Activate(main);

    public InjectionResult RequestSave(IntPtr main)
    {
        Saves++;
        SaveTarget = main;
        OnSave?.Invoke();
        return SaveVerdict;
    }

    public void RequestClose(IntPtr main)
    {
        Closes++;
        OnClose?.Invoke();
    }

    public bool IsSessionAlive(int processId) => Alive;

    public void Terminate(int processId)
    {
        Terminations++;
        Alive = false;
    }
}

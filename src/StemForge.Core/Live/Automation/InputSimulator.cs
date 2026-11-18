using static StemForge.Core.Live.Automation.NativeMethods;

namespace StemForge.Core.Live.Automation;

/// <summary>
/// Synthetic keyboard and mouse input, and nothing else.
///
/// This is the mechanism; <see cref="GuardedInput"/> is the policy. Nothing outside this
/// namespace is supposed to reach it - the methods below are internal so that a call which has not
/// been verified is a compile error rather than a click in the user's window - and what reaches it
/// has already answered "is the desktop interactive" and "is this window the one in front".
///
/// Whether the desktop may receive input at all is still not the caller's job: <see cref="Send"/>
/// refuses to inject anything while Windows owns the screen, which is the last line behind the
/// guard rather than a substitute for it.
///
/// <see cref="PostKey"/> is deliberately not part of this arrangement. It is not synthetic input:
/// it addresses one window's queue, is aimed by handle, and cannot land anywhere else, so it is
/// public, unguarded and the answer the product prefers wherever a window accepts it.
/// </summary>
public static class InputSimulator
{
    internal static void MoveTo(int x, int y)
    {
        var desktop = VirtualDesktop();
        var (dx, dy) = Normalize(x, y, desktop.Left, desktop.Top, desktop.Width, desktop.Height);
        var inputs = new[]
        {
            new INPUT
            {
                type = InputMouse,
                U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk } },
            },
        };
        Send(inputs);
    }

    /// <summary>
    /// Pixels to the units SendInput actually asks for: MOUSEEVENTF_ABSOLUTE takes a position
    /// normalised over the whole virtual desktop, not a coordinate. Passing pixels instead is not
    /// a rounding error - it divides the target by the screen size, so a click aimed at Live's
    /// menu bar lands within a few pixels of the top-left corner of the primary monitor.
    /// </summary>
    public static (int Dx, int Dy) Normalize(int x, int y, int left, int top, int width, int height)
    {
        if (width < 2 || height < 2)
        {
            throw new InvalidOperationException(
                $"Windows reported a {width}x{height} virtual desktop; there is no screen to click on.");
        }

        return ((int)Math.Round((x - left) * 65535.0 / (width - 1)),
                (int)Math.Round((y - top) * 65535.0 / (height - 1)));
    }

    private static (int Left, int Top, int Width, int Height) VirtualDesktop() =>
        (GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
         GetSystemMetrics(SmCxVirtualScreen), GetSystemMetrics(SmCyVirtualScreen));

    internal static void LeftClick(int x, int y)
    {
        MoveTo(x, y);
        Send(new[]
        {
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventLeftDown } } },
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventLeftUp } } },
        });
    }

    internal static void RightClick(int x, int y)
    {
        MoveTo(x, y);
        Send(new[]
        {
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventRightDown } } },
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventRightUp } } },
        });
    }

    internal static void DoubleClick(int x, int y)
    {
        MoveTo(x, y);
        Send(new[]
        {
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventLeftDown } } },
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventLeftUp } } },
        });
        Thread.Sleep(60);
        Send(new[]
        {
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventLeftDown } } },
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventLeftUp } } },
        });
    }

    /// <summary>Presses a single virtual key (e.g. VK_RETURN, VK_ESCAPE, VK_TAB).</summary>
    internal static void KeyPress(ushort virtualKey, int holdMilliseconds = 30)
    {
        Send(new[]
        {
            new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey } } },
        });
        Thread.Sleep(holdMilliseconds);
        Send(new[]
        {
            new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = KeyEventKeyUp } } },
        });
    }

    /// <summary>Holds a modifier, presses a key, then releases the modifier.</summary>
    internal static void Chord(ushort modifier, ushort virtualKey)
    {
        Send(new[]
        {
            new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wVk = modifier } } },
            new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey } } },
        });
        Thread.Sleep(40);
        Send(new[]
        {
            new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = KeyEventKeyUp } } },
            new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wVk = modifier, dwFlags = KeyEventKeyUp } } },
        });
    }

    /// <summary>Types a string using unicode scan codes, so layout does not matter.</summary>
    internal static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            Send(new[]
            {
                new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KeyEventUnicode } } },
                new INPUT { type = InputKeyboard, U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KeyEventUnicode | KeyEventKeyUp } } },
            });
        }
    }

    /// <summary>
    /// Posts a key press into one window's own message queue instead of injecting it into the
    /// desktop.
    ///
    /// This is not synthetic input and does not go through <see cref="Send"/>, which is the point:
    /// it needs neither the foreground nor a visible desktop, moves no cursor, cannot land in the
    /// user's window, and is not refused while the screen is locked. Measured against Live's
    /// recovery question, a posted 'N' answered it and Live carried on loading its control
    /// surfaces, with the pointer where it had been.
    ///
    /// The message goes to the window, so only a window that reads its own keyboard messages
    /// answers it. Live's dialogs do; a click stays the fallback for whatever does not.
    /// </summary>
    public static bool PostKey(IntPtr hWnd, ushort virtualKey)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;

        var key = (IntPtr)virtualKey;
        if (!PostMessageW(hWnd, WmKeyDown, key, IntPtr.Zero)) return false;

        Thread.Sleep(30);
        PostMessageW(hWnd, WmKeyUp, key, IntPtr.Zero);
        return true;
    }

    /// <summary>
    /// How many times this process has tried to inject input into the desktop. One increment site,
    /// here, because <see cref="Send"/> is the only route to <c>SendInput</c> in this assembly.
    ///
    /// It exists so "the posted-key path sends nothing" can be asserted instead of sampled: tests
    /// read it before and after a posted keystroke and a dialog that must be named without the
    /// screen, and a non-zero delta means some path quietly reached for the pointer or the keyboard.
    /// </summary>
    public static int InjectionAttempts { get; private set; }

    private static void Send(INPUT[] inputs)
    {
        InjectionAttempts++;

        // Nothing is injected unless the ordinary desktop is the one receiving input. Watching
        // Live needs no desktop, so this is asked here rather than on every poll of every wait.
        DesktopGuard.Require(string.Empty);

        var size = System.Runtime.InteropServices.Marshal.SizeOf<INPUT>();
        var sent = SendInput((uint)inputs.Length, inputs, size);
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"Windows rejected synthetic input (sent {sent} of {inputs.Length}). Is a higher-integrity window focused?");
        }
    }

    public const ushort VkReturn = 0x0D;
    public const ushort VkEscape = 0x1B;
    public const ushort VkTab = 0x09;
    public const ushort VkSpace = 0x20;
    public const ushort VkLeftAlt = 0xA4;
    public const ushort VkControl = 0x11;
    public const ushort VkShift = 0x10;
    public const ushort VkDown = 0x28;
    public const ushort VkA = 0x41;
    public const ushort VkS = 0x53;
    public const ushort VkUp = 0x26;
    public const ushort VkLeft = 0x25;
    public const ushort VkRight = 0x27;
}

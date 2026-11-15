using static StemForge.Core.Live.Automation.NativeMethods;

namespace StemForge.Core.Live.Automation;

/// <summary>
/// Synthetic keyboard and mouse input. Every method takes window-relative or absolute
/// physical pixels; the caller is responsible for having activated and verified the
/// target window first.
/// </summary>
public static class InputSimulator
{
    public static void MoveTo(int x, int y)
    {
        var inputs = new[]
        {
            new INPUT
            {
                type = InputMouse,
                U = new InputUnion { mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = MouseEventMove | MouseEventAbsolute } },
            },
        };
        Send(inputs);
    }

    public static void LeftClick(int x, int y)
    {
        MoveTo(x, y);
        Send(new[]
        {
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventLeftDown } } },
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventLeftUp } } },
        });
    }

    public static void RightClick(int x, int y)
    {
        MoveTo(x, y);
        Send(new[]
        {
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventRightDown } } },
            new INPUT { type = InputMouse, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseEventRightUp } } },
        });
    }

    public static void DoubleClick(int x, int y)
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
    public static void KeyPress(ushort virtualKey, int holdMilliseconds = 30)
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
    public static void Chord(ushort modifier, ushort virtualKey)
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
    public static void TypeText(string text)
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

    private static void Send(INPUT[] inputs)
    {
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

using System.Windows.Automation;

namespace StemForge.Core.Live.Automation;

/// <summary>A button a dialog offers: Live's own label for it, and where to press it.</summary>
public readonly record struct DialogButton(string Name, AutomationElement Element);

/// <summary>
/// The key that answers one of Live's dialogs without touching the screen.
///
/// Live's message boxes act on a key posted to their own window - see
/// <see cref="InputSimulator.PostKey"/> - which is what turns "a run needs his screen for a few
/// seconds" into "a run needs nothing". The remaining question is which key reaches which button,
/// and getting that wrong on a dialog that decides whether somebody's unsaved set survives is not
/// a mistake this gets to make twice.
/// </summary>
public static class DialogKey
{
    /// <summary>
    /// The virtual key that presses the button called <paramref name="label"/>, or null when no key
    /// can be trusted to reach only that button.
    ///
    /// Live spells a dialog button's key as its first letter: 'N' reaches "No", 'D' reaches
    /// "Don't Recover" - the apostrophe and the curly one in "Don’t" are not letters and are
    /// stepped over. <paramref name="dialogLabels"/> is the whole dialog, the target included,
    /// because the rest of it decides whether the key is safe: the save question measured here was
    /// Close|Yes|No, and a dialog holding both "Close" and "Cancel" is a dialog where 'C' is a coin
    /// toss. Ambiguity is refused rather than guessed at, and the caller clicks instead.
    /// </summary>
    public static ushort? ForLabel(string label, IEnumerable<string> dialogLabels)
    {
        var key = FirstLetterKey(label);
        if (key is null) return null;

        var reachable = dialogLabels.Count(name => FirstLetterKey(name) == key);

        return reachable == 1 ? key : null;
    }

    /// <summary>
    /// The VK code of a label's first letter, and only of a plain A-Z letter. A VK is a physical
    /// key, so an accented or non-Latin first character has no code a posted message can stand in
    /// for; guessing one would press a key that is not on Live's button at all.
    /// </summary>
    private static ushort? FirstLetterKey(string label)
    {
        if (string.IsNullOrEmpty(label)) return null;

        foreach (var ch in label)
        {
            if (ch is >= 'A' and <= 'Z') return (ushort)ch;
            if (ch is >= 'a' and <= 'z') return (ushort)(ch - 32);
            if (char.IsLetter(ch)) return null;
        }

        return null;
    }
}

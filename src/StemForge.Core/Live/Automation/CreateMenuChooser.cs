using System;
using System.Collections.Generic;

namespace StemForge.Core.Live.Automation;

/// <summary>
/// What Live's own accessibility tree says about the Create menu after something claims to
/// have opened it. This is the only answer that can be believed: every other way of opening
/// this menu has been measured replying with success and changing nothing.
/// </summary>
public enum MenuPopupState
{
    /// <summary>No Separate Stems entry anywhere - the menu never opened.</summary>
    NotPoppedUp,

    /// <summary>
    /// The entry exists but is stale, so a menu opened and closed again. Usually an earlier
    /// attempt left it open and a later one toggled it shut.
    /// </summary>
    ClosedAgain,

    /// <summary>The entry is there and disabled: Live is refusing the command, not losing it.</summary>
    GreyedOut,

    /// <summary>The entry is there, live in the tree, and enabled.</summary>
    Pressable,
}

/// <summary>Why the chooser stopped trying.</summary>
public enum MenuChoiceResult
{
    /// <summary>The entry was pressed.</summary>
    Chosen,

    /// <summary>Live refused the command. Pressing it again is not a retry, it is a wait.</summary>
    GreyedOut,

    /// <summary>Every strategy ran and none of them produced a popup.</summary>
    NeverOpened,

    /// <summary>The menu was open and readable, but the entry itself could not be pressed.</summary>
    PressFailed,
}

/// <summary>
/// Opens Live's Create menu by trying several ways of clicking it and believing none of them.
///
/// The shipped code used to ask one strategy whether it had worked and take its word for it.
/// Live's ExpandCollapse/Invoke patterns answer with success whether or not anything happened,
/// so a no-op opened nothing, the run waited out the whole dialog timeout for a popup that had
/// never been asked for, and the fallback clicks were never reached. Here every attempt is
/// followed by reading the popup back out of Live's tree, and an attempt that cannot show a
/// result is simply the next attempt's predecessor.
/// </summary>
public static class CreateMenuChooser
{
    /// <summary>
    /// Runs <paramref name="attempts"/> in order. After each one, <paramref name="observe"/>
    /// reports what the desktop actually looks like; only a real popup is acted on, and only
    /// <see cref="MenuPopupState.Pressable"/> gets as far as <paramref name="pressEntry"/>.
    /// </summary>
    public static MenuChoiceResult Choose(
        IReadOnlyList<(string Name, Action Attempt)> attempts,
        Func<MenuPopupState> observe,
        Func<bool> pressEntry,
        Action<string> note)
    {
        foreach (var (name, attempt) in attempts)
        {
            attempt();

            switch (observe())
            {
                case MenuPopupState.NotPoppedUp:
                    note($"'{name}' did not bring up the Create menu");
                    continue;

                case MenuPopupState.ClosedAgain:
                    note($"'{name}' opened the Create menu and it closed again");
                    continue;

                case MenuPopupState.GreyedOut:
                    note($"Live shows Separate Stems greyed out after '{name}'");
                    return MenuChoiceResult.GreyedOut;
            }

            return pressEntry()
                ? MenuChoiceResult.Chosen
                : MenuChoiceResult.PressFailed;
        }

        return MenuChoiceResult.NeverOpened;
    }
}

using System.Windows.Automation;

namespace StemForge.Core.Live.Automation;

/// <summary>
/// Thin, exception-safe wrapper around Windows UI Automation. Ableton exposes its
/// buttons and toggles as automation elements (its own accessibility strings name them
/// "the vocals button in the stem separation dialog", and so on), so UIA is the first
/// choice for driving the Separate Stems dialog.
/// </summary>
public static class LiveUi
{
    public static AutomationElement? FromHandle(IntPtr hWnd)
    {
        try
        {
            return hWnd == IntPtr.Zero ? null : AutomationElement.FromHandle(hWnd);
        }
        catch
        {
            return null;
        }
    }

    public static AutomationElement? ForegroundRoot()
    {
        try
        {
            var foreground = WindowControl.GetForegroundWindow();
            return FromHandle(foreground);
        }
        catch
        {
            return null;
        }
    }

    public static string Name(AutomationElement element)
    {
        try { return element.Current.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static string AutomationId(AutomationElement element)
    {
        try { return element.Current.AutomationId ?? string.Empty; }
        catch { return string.Empty; }
    }

    public static string HelpText(AutomationElement element)
    {
        try { return element.Current.HelpText ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Live's own answer to "can this be pressed right now". False also covers an element that
    /// could not be read, so a caller that distinguishes "refused" from "gone" checks
    /// <see cref="IsStale"/> first.
    /// </summary>
    public static bool IsEnabled(AutomationElement element)
    {
        try { return element.Current.IsEnabled; }
        catch { return false; }
    }

    public static bool IsOffscreen(AutomationElement element)
    {
        try { return element.Current.IsOffscreen; }
        catch { return true; }
    }

    public static bool IsStale(AutomationElement element)
    {
        try
        {
            _ = element.Current.Name;
            return false;
        }
        catch
        {
            return true;
        }
    }

    public static IReadOnlyList<AutomationElement> Descendants(AutomationElement root, int max = 4000)
    {
        try
        {
            var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            var list = new List<AutomationElement>(Math.Min(all.Count, max));
            for (var i = 0; i < all.Count && list.Count < max; i++)
            {
                list.Add(all[i]);
            }
            return list;
        }
        catch
        {
            return Array.Empty<AutomationElement>();
        }
    }

    public static IReadOnlyList<AutomationElement> Children(AutomationElement root)
    {
        try
        {
            var all = root.FindAll(TreeScope.Children, Condition.TrueCondition);
            var list = new List<AutomationElement>(all.Count);
            for (var i = 0; i < all.Count; i++) list.Add(all[i]);
            return list;
        }
        catch
        {
            return Array.Empty<AutomationElement>();
        }
    }

    /// <summary>
    /// Finds an element by its automation id. Ids are the stable half of Ableton's tree: names
    /// are localised and reworded between versions, but "StatusText" is the bottom-left status
    /// line in every language, which makes it the right thing to read a busy signal from.
    /// </summary>
    public static AutomationElement? FindByAutomationId(AutomationElement root, string id)
    {
        try
        {
            var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            return root.FindFirst(TreeScope.Descendants, condition);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds a control whose name or help text contains any of the given keywords.
    /// </summary>
    public static AutomationElement? FindByText(
        AutomationElement root,
        IEnumerable<string> keywords,
        bool restrictToInteractive = true)
    {
        var wanted = keywords.Select(k => k.ToLowerInvariant()).ToArray();

        foreach (var element in Descendants(root))
        {
            if (restrictToInteractive && !IsInteractive(element)) continue;

            if (Matches(element, wanted)) return element;
        }

        return null;
    }

    /// <summary>
    /// Finds a control anywhere on the desktop whose name / help text / automation id
    /// contains one of the keywords. Used for Live's menu popups, which are drawn in
    /// their own top level window rather than under the main window.
    /// </summary>
    public static AutomationElement? FindAnywhere(IEnumerable<string> keywords, bool interactiveOnly = false)
    {
        try
        {
            return FindByText(AutomationElement.RootElement, keywords, interactiveOnly);
        }
        catch
        {
            return null;
        }
    }

    private static bool Matches(AutomationElement element, string[] lowerKeywords)
    {
        var haystack = (Name(element) + " " + HelpText(element) + " " + AutomationId(element)).ToLowerInvariant();
        return lowerKeywords.Any(haystack.Contains);
    }

    /// <summary>
    /// The session-view slot holding a clip of this name, or null when Live shows none.
    ///
    /// <see cref="FindByText"/> cannot be used here: it matches a substring anywhere in the tree
    /// and returns the first hit, and a clip called "clip" also matches the browser's "Clips"
    /// category, which comes earlier in Live's tree. Clicking that selects nothing in the session,
    /// and Live answers by keeping "Separate Stems to New Audio Tracks" greyed out - which is what
    /// the second job of run-20261115-191232 blamed on the clip.
    /// </summary>
    public static AutomationElement? FindClipSlot(AutomationElement root, string clipName)
    {
        foreach (var element in Descendants(root))
        {
            if (IsClipSlotName(Name(element), clipName)) return element;
        }

        return null;
    }

    /// <summary>
    /// True when a UIA name is Live's own label for the slot holding this clip. Live spells that
    /// label <c>"&lt;clip&gt;, stopped, in track 1-Audio, scene 1"</c>; the state word between the
    /// first comma and "in track" is the part that changes while the clip plays or records.
    /// </summary>
    public static bool IsClipSlotName(string uiName, string clipName)
    {
        if (clipName.Length == 0 || uiName.Length < clipName.Length) return false;

        if (!uiName.AsSpan(0, clipName.Length).Equals(clipName.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = uiName.AsSpan(clipName.Length);
        if (rest.IsEmpty) return true;

        return rest[0] == ',' && rest.Contains("in track".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds a menu item belonging to one process. Live draws its menu popups in their own
    /// top level window (the classic "#32768" menu class), so scanning the process's own
    /// windows is both faster and more reliable than walking the whole desktop.
    /// </summary>
    public static AutomationElement? FindMenuItemForProcess(int processId, string text)
    {
        foreach (var window in LiveProcessWindows.ForProcess(processId))
        {
            if (!window.Visible) continue;

            var isMenuPopup = window.ClassName == "#32768"
                           || string.IsNullOrWhiteSpace(window.Title);
            if (!isMenuPopup) continue;

            var root = FromHandle(window.Handle);
            if (root is null) continue;

            foreach (var element in Descendants(root, 500))
            {
                try
                {
                    if (element.Current.ControlType != ControlType.MenuItem) continue;
                }
                catch
                {
                    continue;
                }

                if (Name(element).ToLowerInvariant().Contains(text.ToLowerInvariant())) return element;
            }
        }

        return null;
    }

    /// <summary>
    /// Waits for a menu item that belongs to one process.
    ///
    /// Live draws its menu popups in their own top-level window, so the process's own windows are
    /// where the item is - and nowhere else needs looking. There used to be a desktop-wide fallback
    /// here, and it cost the run twice over: walking every element on the desktop takes seconds on
    /// a busy machine, which is seconds in which a cancelled batch keeps working, and it could hand
    /// back a stranger's menu item whose name happened to contain the words.
    /// </summary>
    public static AutomationElement? WaitForMenuItem(
        int processId,
        string text,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return WaitFor(() => FindMenuItemForProcess(processId, text), timeout, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Waits for a descendant matching any keyword, optionally within one root.
    /// </summary>
    public static AutomationElement? WaitForText(
        AutomationElement? root,
        IEnumerable<string> keywords,
        TimeSpan timeout,
        bool interactiveOnly = false,
        CancellationToken cancellationToken = default)
    {
        var wanted = keywords.ToArray();
        return WaitFor(
            () => root is null
                ? FindByText(AutomationElement.RootElement, wanted, interactiveOnly)
                : FindByText(root, wanted, interactiveOnly),
            timeout,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Polls a lookup until it returns something, the deadline passes or the caller cancels.
    ///
    /// All three are asked on every round. A UIA read cannot be interrupted once it has started, so
    /// the deadline and the token are checked before each one and the sleep never runs past the
    /// timeout - which is what keeps "wait for a popup" from becoming "wait for the whole batch to
    /// be cancelled and then some".
    /// </summary>
    public static AutomationElement? WaitFor(
        Func<AutomationElement?> find,
        TimeSpan timeout,
        int pollMilliseconds = 150,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var found = find();
                if (found is not null) return found;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // transient UI Automation failures are expected while the UI is being built
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return null;

            Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(pollMilliseconds, remaining.TotalMilliseconds)));
        }
    }

    /// <summary>
    /// Opens a top-level menu from Live's application menu bar. Live exposes the bar as
    /// a MenuBar named "Application" whose items are named File/Edit/Create/View/...
    /// </summary>
    public static bool TryOpenMenu(AutomationElement root, string menuName)
    {
        var item = FindMenuBarItem(root, menuName);
        if (item is null) return false;

        // ExpandCollapse is the natural pattern for a menu; Invoke also works in Live.
        try
        {
            if (item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand) &&
                expand is ExpandCollapsePattern collapse)
            {
                collapse.Expand();
                return true;
            }
        }
        catch
        {
            // fall through
        }

        return TryInvoke(item);
    }

    /// <summary>
    /// The menu-bar entry itself, so a caller can click exactly where Live says it is instead
    /// of guessing a fraction of the window. Live's accessibility tree reports this rectangle
    /// accurately; a click computed from the window frame lands in the title bar.
    /// </summary>
    public static AutomationElement? FindMenuBarItem(AutomationElement root, string menuName)
    {
        foreach (var element in Descendants(root))
        {
            try
            {
                if (element.Current.ControlType != ControlType.MenuItem) continue;
            }
            catch
            {
                continue;
            }

            if (string.Equals(Name(element), menuName, StringComparison.OrdinalIgnoreCase)) return element;
        }

        return null;
    }

    /// <summary>
    /// The window a click on this element would be delivered to: the top-level window the element
    /// lives in, found by asking the element and then its ancestors for a native handle.
    /// <see cref="IntPtr.Zero"/> when the tree cannot say - a stale element whose popup was
    /// destroyed, most often, which is exactly what a caller must not click.
    /// </summary>
    public static IntPtr TopLevelWindowOf(AutomationElement element)
    {
        try
        {
            for (var current = element; current is not null; current = Parent(current))
            {
                var handle = current.Current.NativeWindowHandle;
                if (handle != 0) return GuardedInput.RootOf(new IntPtr(handle));
            }
        }
        catch
        {
            // An element that cannot be read is an element that cannot be aimed at.
        }

        return IntPtr.Zero;
    }

    private static AutomationElement? Parent(AutomationElement element)
    {
        try
        {
            return TreeWalker.ControlViewWalker.GetParent(element);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The verdict <see cref="TryClick"/> reaches for, with no side effect: the element's own
    /// top-level window, compared against what is actually in front and against what is topmost
    /// where the click would land. Kept separate from the click so the rule can be tested against
    /// real windows on a machine somebody is using - a test that reached
    /// <see cref="InputSimulator"/> would move his pointer.
    /// </summary>
    public static string? ClickRefusalReason(AutomationElement element)
    {
        var point = ClickablePoint(element);
        if (point is null) return "no-clickable-point";

        var window = TopLevelWindowOf(element);
        if (window == IntPtr.Zero) return GuardedInput.TargetWindowGone;

        return GuardedInput.RefusalReason(window, point);
    }

    /// <summary>
    /// Presses an element the way a person does - a real mouse click on its own screen rectangle
    /// - instead of through a pattern. Live's accessibility tree mirrors the interface faithfully
    /// but answers pattern calls inconsistently: a clip slot exposes no selection pattern, a menu
    /// item accepts Invoke and does nothing. Anything that reported success and changed nothing
    /// is escalated to this.
    /// </summary>
    public static bool TryClick(AutomationElement element)
    {
        var point = ClickablePoint(element);
        if (point is null) return false;

        // Which window a click lands on is decided by Windows, not by Live's tree, so the question
        // is asked of the window the element actually belongs to: its top-level window has to be
        // both in front and topmost at its own point. Comparing process ids here instead - which is
        // what this did - let Live's own main window vouch for a popup that had already closed, and
        // the click then went into the session view underneath it.
        var window = TopLevelWindowOf(element);
        if (window == IntPtr.Zero) return false;

        return GuardedInput
            .Click(window, point.Value, $"clicked '{Name(element)}'")
            .Injected;
    }

    public static AutomationElement? FindExact(AutomationElement root, string name)
    {
        foreach (var element in Descendants(root))
        {
            if (string.Equals(Name(element), name, StringComparison.OrdinalIgnoreCase))
                return element;
        }
        return null;
    }

    private static bool IsInteractive(AutomationElement element)
    {
        try
        {
            var type = element.Current.ControlType;
            return type == ControlType.Button
                || type == ControlType.CheckBox
                || type == ControlType.RadioButton
                || type == ControlType.ComboBox
                || type == ControlType.MenuItem
                || type == ControlType.TabItem
                || type == ControlType.ListItem
                || type == ControlType.SplitButton
                || type == ControlType.Edit;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Invokes an element through its Invoke pattern only, with no click fallback. For dialogs that
    /// must be answered without the user's screen: a real click lands wherever the pointer is, and
    /// a save must work while nobody is watching it.
    /// </summary>
    public static bool TryInvokePattern(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) &&
                pattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }
        }
        catch
        {
            // fall through
        }

        return false;
    }

    public static bool TryInvoke(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) &&
                pattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }
        }
        catch
        {
            // fall through
        }

        return TryClickViaLegacyPattern(element);
    }

    private static bool TryClickViaLegacyPattern(AutomationElement element)
    {
        try
        {
            return TryClick(element);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a value into an edit control. ValuePattern is the direct route; when a control
    /// refuses it, the field is focused, cleared and typed into instead, which works on any
    /// standard Windows file dialog but needs the desktop, so it is not for a dialog that must be
    /// answered unnoticed.
    ///
    /// The typing half goes through the same door as every other keystroke, aimed at the window the
    /// field lives in: a field whose dialog is not in front does not get the text, because the text
    /// would not go into it.
    /// </summary>
    public static bool TrySetValue(AutomationElement element, string value)
    {
        if (TrySetValueWithoutTyping(element, value)) return true;

        try
        {
            element.SetFocus();

            var window = TopLevelWindowOf(element);
            if (window == IntPtr.Zero) return false;

            if (!GuardedInput.PressChord(window, InputSimulator.VkControl, InputSimulator.VkA,
                    $"cleared '{Name(element)}'").Injected)
                return false;

            return GuardedInput.TypeText(window, value, $"typed into '{Name(element)}'").Injected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a value through ValuePattern and nothing else. A field that refuses it is reported
    /// as failed rather than typed into: typing is synthetic input, and the caller that asks for
    /// this cannot afford a keystroke landing in whichever window happens to be in front.
    /// </summary>
    public static bool TrySetValueWithoutTyping(AutomationElement element, string value)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) &&
                pattern is ValuePattern editable &&
                !editable.Current.IsReadOnly)
            {
                editable.SetValue(value);
                return true;
            }
        }
        catch
        {
            // The caller decides what a refused value means.
        }

        return false;
    }

    /// <summary>
    /// The first visible edit control in a window, without judging whether it is the file-name
    /// field. Used to tell a file dialog from a message box: a dialog with a field is asking for a
    /// path, one without is asking a question, and the two need opposite answers.
    /// </summary>
    public static AutomationElement? FindAnyEdit(AutomationElement root)
    {
        foreach (var element in Descendants(root))
        {
            try
            {
                if (element.Current.ControlType != ControlType.Edit) continue;
                if (IsOffscreen(element)) continue;
                return element;
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    /// <summary>The field a save dialog writes the file name into, or null when there is none.</summary>
    public static AutomationElement? FindFileNameEdit(AutomationElement root)
    {
        AutomationElement? first = null;

        foreach (var element in Descendants(root))
        {
            try
            {
                if (element.Current.ControlType != ControlType.Edit) continue;
                if (IsOffscreen(element)) continue;
                if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out _)) continue;
            }
            catch
            {
                continue;
            }

            first ??= element;

            // The named field beats the positional guess: dialogs carry more than one edit.
            var haystack = (Name(element) + " " + HelpText(element) + " " + AutomationId(element)).ToLowerInvariant();
            if (haystack.Contains("file name") || haystack.Contains("filename") ||
                haystack.Contains("save in") || haystack.Contains("file:") || haystack.Contains("name:"))
            {
                return element;
            }
        }

        return first;
    }

    /// <summary>Reads the current toggle state, or null when the element is not a toggle.</summary>
    public static bool? ToggleState(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) &&
                pattern is TogglePattern toggle)
            {
                return toggle.Current.ToggleState switch
                {
                    System.Windows.Automation.ToggleState.On => true,
                    System.Windows.Automation.ToggleState.Off => false,
                    _ => null,
                };
            }
        }
        catch
        {
            // fall through
        }
        return null;
    }

    public static bool TrySetToggle(AutomationElement element, bool desired)
    {
        try
        {
            var current = ToggleState(element);
            if (current == desired) return true;

            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) &&
                pattern is TogglePattern toggle)
            {
                toggle.Toggle();
                var after = ToggleState(element);
                return after == desired;
            }
        }
        catch
        {
            // fall through to a direct click
        }

        return TryClickViaLegacyPattern(element);
    }

    public static System.Drawing.Point? ClickablePoint(AutomationElement element)
    {
        try
        {
            var rect = element.Current.BoundingRectangle;
            if (rect.IsEmpty) return null;
            return new System.Drawing.Point((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Polls a condition, invoking <paramref name="onTick"/> each round (for abort checks).</summary>
    public static bool WaitUntil(
        Func<bool> predicate,
        TimeSpan timeout,
        TimeSpan poll,
        CancellationToken cancellationToken,
        Action? onTick = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onTick?.Invoke();
            try
            {
                if (predicate()) return true;
            }
            catch
            {
                // transient UIA failure: keep waiting
            }
            Thread.Sleep(poll);
        }

        return false;
    }

    /// <summary>Human readable dump of the automation tree, used in diagnostics.</summary>
    public static string DumpTree(AutomationElement root, int maxDepth = 6, int maxNodes = 400)
    {
        var sb = new System.Text.StringBuilder();
        var count = 0;

        void Walk(AutomationElement element, int depth)
        {
            if (depth > maxDepth || count >= maxNodes) return;
            count++;

            string type;
            try { type = element.Current.ControlType?.ProgrammaticName ?? "?"; }
            catch { type = "?"; }

            sb.Append(new string(' ', depth * 2))
              .Append(type.Replace("ControlType.", string.Empty))
              .Append(" name=\"").Append(Name(element)).Append('"')
              .Append(" id=\"").Append(AutomationId(element)).Append('"')
              .Append(IsEnabled(element) ? " enabled" : " disabled")
              .AppendLine();

            foreach (var child in Children(element))
                Walk(child, depth + 1);
        }

        Walk(root, 0);
        return sb.ToString();
    }
}

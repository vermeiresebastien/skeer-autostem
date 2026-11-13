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

    /// <summary>Waits for a menu item that belongs to the given process, then the desktop.</summary>
    public static AutomationElement? WaitForMenuItem(int processId, string text, TimeSpan timeout)
    {
        return WaitFor(
            () => FindMenuItemForProcess(processId, text) ?? FindMenuItem(text),
            timeout);
    }

    /// <summary>Finds a menu item by its visible text, searching the whole desktop.</summary>
    public static AutomationElement? FindMenuItem(string text)
    {
        var wanted = text.ToLowerInvariant();

        foreach (var element in Descendants(AutomationElement.RootElement))
        {
            try
            {
                if (element.Current.ControlType != ControlType.MenuItem) continue;
            }
            catch
            {
                continue;
            }

            if (Name(element).ToLowerInvariant().Contains(wanted)) return element;
        }

        return null;
    }

    /// <summary>
    /// Waits for a menu item to appear. Popups are created asynchronously after the menu
    /// opens, so a single look immediately afterwards is not reliable.
    /// </summary>
    public static AutomationElement? WaitForMenuItem(string text, TimeSpan timeout)
    {
        return WaitFor(() => FindMenuItem(text), timeout);
    }

    /// <summary>Waits for a descendant matching any keyword, optionally within one root.</summary>
    public static AutomationElement? WaitForText(
        AutomationElement? root,
        IEnumerable<string> keywords,
        TimeSpan timeout,
        bool interactiveOnly = false)
    {
        var wanted = keywords.ToArray();
        return WaitFor(
            () => root is null
                ? FindByText(AutomationElement.RootElement, wanted, interactiveOnly)
                : FindByText(root, wanted, interactiveOnly),
            timeout);
    }

    /// <summary>Polls a lookup until it returns something or the timeout elapses.</summary>
    public static AutomationElement? WaitFor(Func<AutomationElement?> find, TimeSpan timeout, int pollMilliseconds = 150)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                var found = find();
                if (found is not null) return found;
            }
            catch
            {
                // transient UI Automation failures are expected while the UI is being built
            }

            if (DateTime.UtcNow >= deadline) return null;
            Thread.Sleep(pollMilliseconds);
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

    private static AutomationElement? FindMenuBarItem(AutomationElement root, string menuName)
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

    /// <summary>Selects an element using whichever pattern it supports, then clicking.</summary>
    public static bool TrySelect(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) &&
                pattern is SelectionItemPattern selection)
            {
                selection.Select();
                return true;
            }
        }
        catch
        {
            // fall through
        }

        if (TryInvoke(element)) return true;

        var point = ClickablePoint(element);
        if (point is null) return false;

        InputSimulator.LeftClick(point.Value.X, point.Value.Y);
        return true;
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
            var point = ClickablePoint(element);
            if (point is null) return false;
            InputSimulator.LeftClick(point.Value.X, point.Value.Y);
            return true;
        }
        catch
        {
            return false;
        }
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

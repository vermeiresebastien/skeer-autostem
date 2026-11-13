using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace StemForge.App.Theme;

/// <summary>
/// Letter-spacing for <see cref="TextBlock"/>. The SKEER appearance system sets explicit
/// tracking on its small uppercase labels (0.09em) and buttons (0.06em), and WPF has no
/// equivalent property, so the text is re-emitted with hair spaces between characters.
///
/// Only use this on labels with static or simple bound text: it rewrites
/// <see cref="TextBlock.Text"/>, so a two-way bound TextBlock would fight it.
/// </summary>
public static class Typography
{
    private static readonly ConditionalWeakTable<TextBlock, Entry> Tracked = new();

    public static readonly DependencyProperty TrackingProperty =
        DependencyProperty.RegisterAttached(
            "Tracking",
            typeof(double),
            typeof(Typography),
            new PropertyMetadata(0.0, OnTrackingChanged));

    public static void SetTracking(DependencyObject element, double value) =>
        element.SetValue(TrackingProperty, value);

    public static double GetTracking(DependencyObject element) =>
        (double)element.GetValue(TrackingProperty);

    private static void OnTrackingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        if (e.NewValue is not double em || em <= 0) return;
        if (Tracked.TryGetValue(block, out _)) return;

        var entry = new Entry();

        // Watch the text so a later assignment is tracked as well.
        entry.Descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        entry.Handler = (_, _) => Apply(block, entry);
        entry.Descriptor.AddValueChanged(block, entry.Handler);

        Tracked.Add(block, entry);
        Apply(block, entry);
    }

    private static void Apply(TextBlock block, Entry entry)
    {
        if (entry.Applying) return;

        var current = block.Text ?? string.Empty;

        // If the current value is already tracked, the untracked original is what we kept.
        if (entry.Source is null || current != Spaced(current))
            entry.Source = current;

        entry.Applying = true;
        try
        {
            block.Text = Spaced(entry.Source);
        }
        finally
        {
            entry.Applying = false;
        }
    }

    private static string Spaced(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length == 1) return text;

        var builder = new StringBuilder(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            builder.Append(text[i]);
            if (i < text.Length - 1 && text[i] != ' ') builder.Append('\u200A');   // hair space
        }

        return builder.ToString();
    }

    private sealed class Entry
    {
        public string? Source { get; set; }
        public DependencyPropertyDescriptor? Descriptor { get; set; }
        public EventHandler? Handler { get; set; }
        public bool Applying { get; set; }
    }
}

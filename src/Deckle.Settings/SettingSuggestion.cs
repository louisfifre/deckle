using System.Collections.Generic;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Deckle.Settings;

// ── SettingSuggestion ─────────────────────────────────────────────────────────
//
// What the search AutoSuggestBox renders. Two row shapes share one ItemsSource: a
// navigable hit and a terminal, non-navigating notice ("+N more", "No results").
// The common base lets the template selector and the chosen-suggestion handler tell
// them apart by type, without a discriminator flag.
public abstract class SettingSuggestion { }

public sealed class SettingSuggestionHit : SettingSuggestion
{
    public required string PageTag { get; init; }
    public required string CardTag { get; init; }
    public required string Glyph { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<SuggestionTextSegment> LabelSegments { get; init; }
    public required string Secondary { get; init; }
}

public sealed class SettingSuggestionNotice : SettingSuggestion
{
    public required string Text { get; init; }
}

public sealed record SuggestionTextSegment(string Text, bool IsMatch);

// ── SuggestionText ────────────────────────────────────────────────────────────
//
// TextHighlighters can only recolour a range, not embolden it, so partial bold has
// to go through Runs; the fast-path text rendering it forfeits is immaterial on a
// handful of rows. Bound OneTime from the hit template; the row objects are never
// mutated in place.
public static class SuggestionText
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.RegisterAttached(
            "Segments",
            typeof(IReadOnlyList<SuggestionTextSegment>),
            typeof(SuggestionText),
            new PropertyMetadata(null, OnSegmentsChanged));

    public static void SetSegments(DependencyObject element, IReadOnlyList<SuggestionTextSegment> value)
        => element.SetValue(SegmentsProperty, value);

    public static IReadOnlyList<SuggestionTextSegment> GetSegments(DependencyObject element)
        => (IReadOnlyList<SuggestionTextSegment>)element.GetValue(SegmentsProperty);

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text) return;

        text.Inlines.Clear();
        if (e.NewValue is not IReadOnlyList<SuggestionTextSegment> segments) return;

        foreach (SuggestionTextSegment segment in segments)
        {
            var run = new Run { Text = segment.Text };
            if (segment.IsMatch) run.FontWeight = FontWeights.SemiBold;
            text.Inlines.Add(run);
        }
    }
}

public sealed partial class SettingSuggestionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HitTemplate { get; set; }
    public DataTemplate? NoticeTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => item is SettingSuggestionNotice ? NoticeTemplate : HitTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}

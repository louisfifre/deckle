using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

// ── InfoBarLayout ─────────────────────────────────────────────────────────────
//
// `catalog:InfoBarLayout.CollapseWhenClosed="True"` on an InfoBar collapses
// it while IsOpen is false. A closed InfoBar takes no height but stays
// Visible, and a StackPanel's Spacing counts every Visible child: each closed
// bar between two cards added one 4 px gap, five in a row on the Recording
// page, which read as a hole in the section (2026-10-14). Visibility follows
// IsOpen both ways through a property-changed callback, so a bar opened from
// code or from a bind reappears in the flow.
public static class InfoBarLayout
{
    public static readonly DependencyProperty CollapseWhenClosedProperty = DependencyProperty.RegisterAttached(
        "CollapseWhenClosed", typeof(bool), typeof(InfoBarLayout),
        new PropertyMetadata(false, OnCollapseWhenClosedChanged));

    public static bool GetCollapseWhenClosed(DependencyObject element)
        => (bool)element.GetValue(CollapseWhenClosedProperty);

    public static void SetCollapseWhenClosed(DependencyObject element, bool value)
        => element.SetValue(CollapseWhenClosedProperty, value);

    private static void OnCollapseWhenClosedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not InfoBar bar || !(bool)e.NewValue) return;
        Follow(bar);
        bar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (_, _) => Follow(bar));
    }

    private static void Follow(InfoBar bar)
        => bar.Visibility = bar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
}

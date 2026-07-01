using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Deckle.Llm.Rewrite;

// ── bool → Visibility ────────────────────────────────────────────────────────
//
// true → Visible, false → Collapsed. Drives the mask-not-grey gating of the
// LLM page: when rewriting is off, the dependent sections (endpoint, shortcut
// slots, rules, profiles, models) collapse out of the layout entirely instead
// of greying. No local BoolToVisibility converter exists app-wide, and the
// project idiom is a plain IValueConverter instantiated as a StaticResource, so
// it lives here alongside the page that needs it.
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

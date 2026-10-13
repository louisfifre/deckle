using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Deckle.Llm.Rewrite;

// ── bool → Visibility ────────────────────────────────────────────────────────
//
// Drives the mask-not-grey gating of the LLM page: when rewriting is off, the
// dependent sections collapse out of the layout instead of greying.
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

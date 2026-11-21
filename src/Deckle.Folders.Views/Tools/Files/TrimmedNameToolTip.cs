using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

// A name cut by its ellipsis shows its full text in a tooltip, and a name
// shown whole shows none, as File Explorer and the WinUI Gallery do (spec,
// Open 33: a name too long for its place). Set on a TextBlock of an item
// template: TextBlock.IsTextTrimmed says whether the ellipsis is drawn, and
// changes with the width the column gives the text.
//
// A display recycles its containers, so the same TextBlock takes another
// name while it may stay cut: the tooltip follows the text as well as the
// ellipsis, or it would show the name the container held before.
public static class TrimmedNameToolTip
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TrimmedNameToolTip), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock text || e.NewValue is not true) return;
        text.IsTextTrimmedChanged += (sender, _) => Update(sender);
        text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (sender, _) => Update((TextBlock)sender));
        Update(text);
    }

    private static void Update(TextBlock text) =>
        ToolTipService.SetToolTip(text, text.IsTextTrimmed ? text.Text : null);
}

using Microsoft.UI.Xaml.Controls;

namespace Deckle.Projects.Views;

// A name cut by its ellipsis shows its full text in a tooltip, and a name shown
// whole shows none, as File Explorer and the WinUI Gallery do. TextBlock
// reports whether the ellipsis is drawn (IsTextTrimmed) and when that changes
// with the width the layout gives it. The tooltip follows the text as well as
// the ellipsis: a card is updated in place when a project is renamed.
internal static class TrimmedTextToolTip
{
    public static void Attach(TextBlock text)
    {
        text.IsTextTrimmedChanged += (sender, _) => Update((TextBlock)sender);
        text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (sender, _) => Update((TextBlock)sender));
        Update(text);
    }

    private static void Update(TextBlock text) =>
        ToolTipService.SetToolTip(text, text.IsTextTrimmed ? text.Text : null);
}

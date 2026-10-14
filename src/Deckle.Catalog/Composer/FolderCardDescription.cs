using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Catalog;

// ── FolderCardDescription ─────────────────────────────────────────────────────
//
// The description of a card that holds a folder: the sentence the .resw gives
// it, then the folder's path on a line of its own, both in the description's
// type and colour (inherited from the card's description presenter). The
// PowerToys form (General, Backup location): what the folder is for, then
// where it is, and the actions at the trailing edge like any other value. The
// earlier form put the path and the buttons on a row of their own under the
// header, which left an empty band above them (Louis, 2026-10-14, on screen).
//
// Used by the hand-authored Data folder card of the General page alone: the
// composed Path settings no longer put the path under the header, they render
// it on a Location line of its own (SettingsComposer.BuildPathLine, Louis,
// 2026-10-15). This class goes with that last usage. The path line never
// wraps: a long path is trimmed with an ellipsis and the tooltip carries it
// whole; the text can be selected to copy it.
public static class FolderCardDescription
{
    // Replaces the card's description with the sentence (when there is one)
    // over the path line, and returns that line for Show.
    public static TextBlock Attach(SettingsCard card, string? sentence)
    {
        var line = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsTextSelectionEnabled = true,
        };

        var panel = new StackPanel { Spacing = 2 };
        if (!string.IsNullOrEmpty(sentence))
            panel.Children.Add(new TextBlock { Text = sentence, TextWrapping = TextWrapping.WrapWholeWords });
        panel.Children.Add(line);
        card.Description = panel;
        return line;
    }

    public static void Show(TextBlock line, string path)
    {
        line.Text = path;
        ToolTipService.SetToolTip(line, string.IsNullOrEmpty(path) ? null : path);
    }
}

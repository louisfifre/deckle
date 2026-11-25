using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

// The dialog that names a folder group, for a new one and for a rename: a
// name field, a line that says when the name is taken, and Create or Save,
// enabled only once the name can be kept. It asks nothing of the store: the
// window passes the store's own check of the names, and gets the chosen name
// back, or null when the person cancelled.
internal static class FolderGroupDialog
{
    // "current" is the group's name when renaming and null for a new group.
    // "isTaken" says whether another group already has a (trimmed) name; the
    // group being renamed is the window's to leave out of it. "shown" hears the
    // dialog once it is open; the self-check uses it to type and to close it.
    public static async Task<string?> AskAsync(
        XamlRoot xamlRoot, string? current, Func<string, bool> isTaken, Action<ContentDialog>? shown = null)
    {
        bool renaming = current is not null;
        var box = new TextBox
        {
            Header = FoldersText.Get("GroupDialog_Name"),
            Text = current ?? "",
        };
        var taken = new TextBlock
        {
            Text = FoldersText.Get("GroupDialog_Taken"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = FoldersText.Get(renaming ? "GroupDialog_RenameTitle" : "GroupDialog_NewTitle"),
            Content = new StackPanel { Spacing = 8, Children = { box, taken } },
            PrimaryButtonText = FoldersText.Get(renaming ? "GroupDialog_Save" : "GroupDialog_Create"),
            CloseButtonText = FoldersText.Get("GroupDialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        // Create stays off for an empty name and for a taken one, which the
        // line under the field says; the person never meets a refusal after
        // pressing it.
        void Refresh()
        {
            string name = box.Text.Trim();
            bool isNameTaken = name.Length > 0 && isTaken(name);
            taken.Visibility = isNameTaken ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = name.Length > 0 && !isNameTaken;
        }
        box.TextChanged += (_, _) => Refresh();
        Refresh();

        // The field has the keyboard when the dialog opens, its text selected
        // for a rename, and Enter commits through the default button.
        dialog.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
            shown?.Invoke(dialog);
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }
}

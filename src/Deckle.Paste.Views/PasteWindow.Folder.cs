using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Paste.Views;

// The folder pastes go to: shown trimmed with its whole path in a tooltip,
// chosen with the system folder picker, kept by PasteFolderStore in the
// window's data folder.
public sealed partial class PasteWindow
{
    private readonly PasteFolderStore _folderStore;

    private void ShowFolder()
    {
        string? folder = _folderStore.Folder;
        string shown = string.IsNullOrEmpty(folder) ? PasteText.Get("Paste_Folder_None") : folder;
        FolderPath.Text = shown;
        ToolTipService.SetToolTip(FolderPath, string.IsNullOrEmpty(folder) ? null : folder);
        AutomationProperties.SetName(FolderPath, $"{PasteText.Get("Paste_Folder_Name")}: {shown}");
    }

    // The Windows App SDK picker (Microsoft.Windows.Storage.Pickers), as
    // FolderLocationLine uses it: built on this window's id, it needs no
    // InitializeWithWindow and works under elevation.
    private async void OnChooseFolderClick(object sender, RoutedEventArgs e)
    {
        string? chosen;
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            };
            var result = await picker.PickSingleFolderAsync();
            chosen = result?.Path;
        }
        catch (Exception)
        {
            // The picker could not open: nothing chosen, nothing changed.
            return;
        }
        if (string.IsNullOrEmpty(chosen)) return;

        try
        {
            _folderStore.SetFolder(chosen);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(PasteStatus.Failed, PasteText.Get("Paste_Error_FolderNotSaved"));
            return;
        }

        ShowFolder();
        // A failure that asked for a folder is answered: the line clears.
        if (FailedMark.Visibility == Visibility.Visible) ShowStatus(PasteStatus.None, null);
        PasteZone.Focus(FocusState.Programmatic);
    }
}

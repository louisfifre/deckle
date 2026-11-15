using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Atelier.Views;

// What the file tool asks of Windows for one item: open a file in its
// application, show an item in File Explorer, send it to the Recycle Bin.
// Each runs off the UI thread, since the shell blocks, and says in a bar
// above the list what Windows did not do, in a sentence that names the item.
// What went as asked says nothing, as File Explorer says nothing.
//
// The Recycle Bin asks no question first: Windows 11's File Explorer sends
// an item there without one, and the bin is where it is restored from.
internal sealed partial class FilesView
{
    private readonly IRecycleBin _bin;
    private readonly IFileLauncher _launcher;

    // Rows whose item is on its way to the bin, so a second press on the same
    // row while the shell works asks for nothing more.
    private readonly HashSet<FileRow> _recycling = [];

    private void OnRowShowInFolderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FileRow row) _ = ShowInFolderAsync(row);
    }

    private void OnRowRecycleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FileRow row) _ = RecycleAsync(row);
    }

    private async Task OpenAsync(FileRow row)
    {
        string path = row.Entry.Path;
        LaunchOutcome outcome = await Task.Run(() => _launcher.Open(path));
        Report(row, outcome, "Files_Open_Failed_Format");
    }

    private async Task ShowInFolderAsync(FileRow row)
    {
        string path = row.Entry.Path;
        LaunchOutcome outcome = await Task.Run(() => _launcher.ShowInFolder(path));
        Report(row, outcome, "Files_ShowInFolder_Failed_Format");
    }

    // An item gone from its folder leaves the list as well, as File Explorer
    // drops it on its next look.
    private void Report(FileRow row, LaunchOutcome outcome, string failedKey)
    {
        switch (outcome.Status)
        {
            case LaunchStatus.Missing:
                RemoveRow(row);
                ShowNotice(InfoBarSeverity.Warning, AtelierText.Format("Files_Gone_Format", row.Name));
                break;
            case LaunchStatus.Failed:
                ShowNotice(InfoBarSeverity.Warning, AtelierText.Format(failedKey, row.Name));
                break;
        }
    }

    // "Mettre à la Corbeille" and the Delete key. The row leaves the list once
    // the item has left its folder, and a selection on it clears with its
    // panel; an item Windows kept keeps its row, and the bar says so.
    internal async Task RecycleAsync(FileRow row)
    {
        if (!_recycling.Add(row)) return;
        RecycleOutcome outcome;
        try
        {
            string path = row.Entry.Path;
            outcome = await Task.Run(() => _bin.RecycleItem(path));
        }
        finally
        {
            _recycling.Remove(row);
        }

        if (outcome.LeftItsFolder) RemoveRow(row);
        switch (outcome.Status)
        {
            case RecycleStatus.Refused or RecycleStatus.Failed or RecycleStatus.NotDone:
                ShowNotice(InfoBarSeverity.Warning, AtelierText.Format("Files_Recycle_Stayed_Format", row.Name));
                break;
            case RecycleStatus.DeletedForGood:
                ShowNotice(InfoBarSeverity.Error, AtelierText.Format("Files_Recycle_Destroyed_Format", row.Name));
                break;
            default:
                Notice.IsOpen = false;
                break;
        }
    }

    // The keyboard stays in the list: the focus goes to the row that took the
    // place of the one removed, or to the new last one, so Delete can be
    // pressed again on the next item without reaching for the pointer.
    private void RemoveRow(FileRow row)
    {
        int index = _rows.IndexOf(row);
        if (index < 0) return;
        ListViewBase items = ItemsOf(Display);
        bool wasSelected = ReferenceEquals(items.SelectedItem, row);
        FocusState focus = FocusWithin(items);

        row.Release(Display);
        _filling = true;
        try
        {
            _rows.RemoveAt(index);
            if (wasSelected) items.SelectedItem = null;
        }
        finally
        {
            _filling = false;
        }
        if (wasSelected) _ = ShowDetailsAsync(null);
        if (_rows.Count == 0) ShowMessage(AtelierText.Get("Files_Empty"));

        // The focus moves the way it came: a key press keeps a keyboard focus,
        // so the next row shows its actions; a click does not.
        if (focus == FocusState.Unfocused) return;
        FocusState next = focus == FocusState.Keyboard ? FocusState.Keyboard : FocusState.Programmatic;
        int at = Math.Min(index, _rows.Count - 1);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (at >= 0 && items.ContainerFromIndex(at) is Control container) container.Focus(next);
            else items.Focus(FocusState.Programmatic);
        });
    }

    // How the focused element got its focus, when it is inside the element;
    // Unfocused when the focus is elsewhere.
    private static FocusState FocusWithin(UIElement element)
    {
        if (element.XamlRoot is null) return FocusState.Unfocused;
        object? focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(element.XamlRoot);
        for (DependencyObject? e = focused as DependencyObject; e is not null; e = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(e))
        {
            if (ReferenceEquals(e, element)) return (focused as UIElement)?.FocusState ?? FocusState.Programmatic;
        }
        return FocusState.Unfocused;
    }

    private void ShowNotice(InfoBarSeverity severity, string message)
    {
        Notice.Severity = severity;
        Notice.Message = message;
        Notice.IsOpen = true;
    }
}

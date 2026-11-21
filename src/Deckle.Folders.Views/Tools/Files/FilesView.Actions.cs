using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Folders.Views;

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
                ShowNotice(InfoBarSeverity.Warning, FoldersText.Format("Files_Gone_Format", row.Name));
                break;
            case LaunchStatus.Failed:
                ShowNotice(InfoBarSeverity.Warning, FoldersText.Format(failedKey, row.Name));
                break;
        }
    }

    // "Move to Recycle Bin" on one row, and the Delete key on every row
    // selected, in one call to the bin, which recycles them one by one so a
    // refusal stops only its own item. A row leaves the list once its item
    // has left its folder, and leaves the selection and its panel with it; an
    // item Windows kept keeps its row, and the bar says so: by its name when
    // it is one, by their number when they are several.
    internal Task RecycleAsync(FileRow row) => RecycleAsync([row]);

    internal async Task RecycleAsync(IReadOnlyList<FileRow> rows)
    {
        FileRow[] going = [.. rows.Where(_recycling.Add)];
        if (going.Length == 0) return;
        string[] paths = [.. going.Select(r => r.Entry.Path)];
        // An outcome the bin never gave reads as not done, so its row stays.
        RecycleOutcome[] outcomes = [.. going.Select(_ => new RecycleOutcome(RecycleStatus.NotDone))];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await Task.Run(() => _bin.Recycle(paths, (i, outcome) => outcomes[i] = outcome, CancellationToken.None));
        }
        finally
        {
            foreach (FileRow row in going) _recycling.Remove(row);
        }

        RemoveRows([.. going.Where((_, i) => outcomes[i].LeftItsFolder)]);
        FileRow[] stayed = [.. going.Where((_, i) => outcomes[i].Status is RecycleStatus.Refused or RecycleStatus.Failed or RecycleStatus.NotDone)];
        FileRow[] destroyed = [.. going.Where((_, i) => outcomes[i].Status is RecycleStatus.DeletedForGood)];
        DeckleFoldersSource.Log.ItemsRecycled(
            going.Length, outcomes.Count(o => o.Status == RecycleStatus.Recycled), stayed.Length, destroyed.Length,
            clock.ElapsedMilliseconds);

        if (destroyed.Length > 0)
            ShowNotice(InfoBarSeverity.Error, Sentence(destroyed, "Files_Recycle_Destroyed_Format", "Files_Recycle_Destroyed_Many_Format"));
        else if (stayed.Length > 0)
            ShowNotice(InfoBarSeverity.Warning, Sentence(stayed, "Files_Recycle_Stayed_Format", "Files_Recycle_Stayed_Many_Format"));
        else
            Notice.IsOpen = false;
    }

    private static string Sentence(FileRow[] rows, string oneKey, string manyKey) =>
        rows.Length == 1 ? FoldersText.Format(oneKey, rows[0].Name) : FoldersText.Format(manyKey, rows.Length);

    private void RemoveRow(FileRow row) => RemoveRows([row]);

    // The keyboard stays in the list: the focus goes to the row that took the
    // place of the first one removed, or to the new last one, so Delete can
    // be pressed again on the next item without reaching for the pointer.
    private void RemoveRows(IReadOnlyList<FileRow> rows)
    {
        int index = rows.Select(_rows.IndexOf).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        if (index < 0) return;
        ListViewBase items = ItemsOf(Display);
        var selected = new HashSet<FileRow>(SelectedRows());
        bool selectionChanged = rows.Any(selected.Contains);
        FocusState focus = FocusWithin(items);

        _filling = true;
        try
        {
            foreach (FileRow row in rows)
            {
                row.Release(Display);
                _rows.Remove(row);
            }
        }
        finally
        {
            _filling = false;
        }
        if (selectionChanged) _ = ShowSelectionAsync();
        if (_rows.Count == 0) ShowMessage(FoldersText.Get("Files_Empty"));

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

using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Deckle.Core;

namespace Deckle.Folders.Views;

// The file tool's centre, in the display chosen at its bottom right: the
// list, the large rows or the gallery. A click selects an item and opens the
// details panel; it never opens the item (spec, Behaviours). A double-click
// or Enter enters a folder and opens a file in its default application, in
// every display, as File Explorer does.
//
// Several items are selected as in File Explorer (spec, Open 33: Windows'
// conventions): Ctrl+click, Shift+click, Ctrl+A and Shift+arrows, the
// controls' own Extended selection. The panel then says how many and what
// they weigh (SelectionDetails), and Delete sends them all to the Recycle
// Bin. The window keeps one path across tools (IFoldersToolHost.Select): the
// last item selected, so another tool shows the one the person last chose.
//
// The three displays show the same items (FileRow). Only the shown one holds
// them, so only its containers are realized: each is a ListView or a GridView,
// which realizes the items on screen alone, and asks the shell for an item's
// icon or picture when it realizes its container (FileRow.LoadAsync), off the
// UI thread behind the icon seam. A folder of thousands of items scrolls as
// one of ten in each display.
//
// This file holds the displays, the selection and the keys; the row actions
// (FilesView.Actions.cs), the panel of a photo (FilesView.Photo.cs) and the
// self-check's hooks (FilesView.Review.cs) each have their partial file.
internal sealed partial class FilesView : UserControl, IFoldersToolView
{
    private readonly IFoldersToolHost _host;
    private FolderPlace? _place;
    private ObservableCollection<FileRow> _rows = [];

    // Set while the items are replaced or moved to another display, so the
    // selection changes a control makes by itself are not taken for the
    // person's.
    private bool _filling;

    // The item last added to the selection, the path the window keeps.
    private FileRow? _lastSelected;

    public FilesView(IFoldersToolHost host, string moduleDirectory, PhotoMarkQueue marks,
                     IRecycleBin bin, IFileLauncher launcher, PhotoDescriptions? descriptions = null)
    {
        _host = host;
        _moduleDirectory = moduleDirectory;
        _marks = marks;
        _bin = bin;
        _launcher = launcher;
        _descriptions = descriptions;
        InitializeComponent();
        Label(DisplayListButton, "Display_List");
        Label(DisplayLargeRowsButton, "Display_LargeRows");
        Label(DisplayGalleryButton, "Display_Gallery");
        AutomationProperties.SetName(DisplayChoice, FoldersText.Get("Display_Group"));
        AutomationProperties.SetName(Rows, FoldersText.Get("Display_List"));
        AutomationProperties.SetName(LargeRows, FoldersText.Get("Display_LargeRows"));
        AutomationProperties.SetName(Gallery, FoldersText.Get("Display_Gallery"));
    }

    FrameworkElement IFoldersToolView.Content => this;

    // Kept by the view, which the window keeps for the tool's whole life: the
    // display stays as chosen from one folder to the next, as File Explorer
    // keeps the last one chosen (spec silent).
    internal FileListDisplay Display { get; private set; } = FileListDisplay.List;

    public async Task ShowAsync(FolderPlace place, string? selection, CancellationToken cancellation)
    {
        FolderListingResult listing = await Task.Run(() => FolderListing.List(place.Path), cancellation);
        if (cancellation.IsCancellationRequested) return;

        if (_place is null || !FolderPaths.Comparer.Equals(_place.Path, place.Path))
        {
            _readings.Clear();
            Notice.IsOpen = false;
        }
        _place = place;
        foreach (FileRow old in _rows) old.Release(Display);
        ObservableCollection<FileRow> rows = [.. listing.Entries.Select(e => new FileRow(e))];
        _rows = rows;
        FileRow? selected;
        _filling = true;
        try
        {
            ListViewBase items = ItemsOf(Display);
            items.ItemsSource = rows;
            selected = selection is null
                ? null
                : rows.FirstOrDefault(r => FolderPaths.Comparer.Equals(r.Entry.Path, selection));
            items.SelectedItem = selected;
            if (selected is not null) items.ScrollIntoView(selected);
        }
        finally
        {
            _filling = false;
        }
        _lastSelected = selected;

        string? message = listing.Failure switch
        {
            FolderListingFailure.None => rows.Count == 0 ? FoldersText.Get("Files_Empty") : null,
            FolderListingFailure.NotFound => FoldersText.Get("Files_NotFound"),
            FolderListingFailure.AccessDenied => FoldersText.Get("Files_AccessDenied"),
            _ => FoldersText.Get("Files_Unreadable"),
        };
        ShowMessage(message);
        await ShowDetailsAsync(selected);
    }

    private void ShowMessage(string? message)
    {
        Message.Text = message ?? "";
        Message.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Displays ──────────────────────────────────────────────────────────

    private void OnDisplayClick(object sender, RoutedEventArgs e)
    {
        FileListDisplay display =
            ReferenceEquals(sender, DisplayLargeRowsButton) ? FileListDisplay.LargeRows
            : ReferenceEquals(sender, DisplayGalleryButton) ? FileListDisplay.Gallery
            : FileListDisplay.List;
        ShowDisplay(display);
    }

    // The same folder in another display: the items move to its control with
    // their whole selection, scrolled to the last item selected, and the
    // details panel is left as it is, since what is selected has not
    // changed. A click on the pressed button would release it; it stays
    // pressed instead, as one display is always shown.
    internal void ShowDisplay(FileListDisplay display)
    {
        MarkDisplay(display);
        if (display == Display) return;

        ListViewBase from = ItemsOf(Display);
        ListViewBase to = ItemsOf(display);
        int[] selected = [.. SelectedIndexes(from)];
        bool hadFocus = from.FocusState != FocusState.Unfocused;
        FileListDisplay left = Display;
        Display = display;

        _filling = true;
        try
        {
            from.ItemsSource = null;
            from.Visibility = Visibility.Collapsed;
            foreach (FileRow row in _rows) row.Release(left);

            to.Visibility = Visibility.Visible;
            to.ItemsSource = _rows;
            SelectIndexes(to, selected);
            if (_lastSelected is not null && selected.Length > 0) to.ScrollIntoView(_lastSelected);
        }
        finally
        {
            _filling = false;
        }

        Header.Visibility = display == FileListDisplay.List ? Visibility.Visible : Visibility.Collapsed;
        if (hadFocus) to.Focus(FocusState.Programmatic);
    }

    // Selects the items at these indexes, in ascending order, one range per
    // run of neighbours: Ctrl+A on a folder of thousands is one range, not
    // thousands of single additions.
    private static void SelectIndexes(ListViewBase items, int[] indexes)
    {
        int start = 0;
        for (int i = 1; i <= indexes.Length; i++)
        {
            if (i < indexes.Length && indexes[i] == indexes[i - 1] + 1) continue;
            items.SelectRange(new ItemIndexRange(indexes[start], (uint)(i - start)));
            start = i;
        }
    }

    private void MarkDisplay(FileListDisplay display)
    {
        DisplayListButton.IsChecked = display == FileListDisplay.List;
        DisplayLargeRowsButton.IsChecked = display == FileListDisplay.LargeRows;
        DisplayGalleryButton.IsChecked = display == FileListDisplay.Gallery;
    }

    private ListViewBase ItemsOf(FileListDisplay display) => display switch
    {
        FileListDisplay.LargeRows => LargeRows,
        FileListDisplay.Gallery => Gallery,
        _ => Rows,
    };

    private FileListDisplay DisplayOf(object control) =>
        ReferenceEquals(control, LargeRows) ? FileListDisplay.LargeRows
        : ReferenceEquals(control, Gallery) ? FileListDisplay.Gallery
        : FileListDisplay.List;

    // ── Icons and pictures ────────────────────────────────────────────────

    // An item asks for what its display draws when the display realizes its
    // container, and drops it when the container is recycled. The sizes are
    // the display's image cells in physical pixels.
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not FileRow row) return;
        FileListDisplay display = DisplayOf(sender);
        if (args.InRecycleQueue)
        {
            row.Release(display);
            return;
        }
        double cell = display switch
        {
            FileListDisplay.LargeRows => LargeIconCellSize,
            FileListDisplay.Gallery => TileIconCellSize,
            _ => IconCellSize,
        };
        _ = row.LoadAsync(_host.Icons, display, PixelSize(cell), PixelSize(TileWidth));
    }

    private const double IconCellSize = 16;
    private const double LargeIconCellSize = 32;
    private const double TileIconCellSize = 64;
    private const double TileWidth = 160;

    private int PixelSize(double dips) => (int)Math.Round(dips * (XamlRoot?.RasterizationScale ?? 1.0));

    // ── Selection and the panel ───────────────────────────────────────────

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;
        if (e.AddedItems.OfType<FileRow>().LastOrDefault() is { } added) _lastSelected = added;
        _ = ShowSelectionAsync();
    }

    // The selection of the shown display, in the folder's order. Read from
    // its ranges of indexes, which Ctrl+A on thousands of items keeps as one.
    private List<FileRow> SelectedRows() =>
        [.. SelectedIndexes(ItemsOf(Display)).Where(i => i < _rows.Count).Select(i => _rows[i])];

    private static IEnumerable<int> SelectedIndexes(ListViewBase items) =>
        items.SelectedRanges
            .OrderBy(r => r.FirstIndex)
            .SelectMany(r => Enumerable.Range(r.FirstIndex, (int)r.Length));

    // One item has its own panel; several, the panel of the group, handed to
    // the window with the last one selected as the path it keeps. An item
    // that left the selection is no longer the last one: the last in the
    // folder's order then is.
    private Task ShowSelectionAsync()
    {
        List<FileRow> selected = SelectedRows();
        if (_lastSelected is null || !selected.Contains(_lastSelected)) _lastSelected = selected.LastOrDefault();
        if (selected.Count <= 1 || _place is null) return ShowDetailsAsync(_lastSelected);

        ForgetPhotoPanel();
        _host.Select(_lastSelected!.Entry.Path, SelectionDetails.For([.. selected.Select(r => r.Entry)], _place.Path));
        return Task.CompletedTask;
    }

    // A photo shows the photo tool's panel (spec, Details panel; 12.15); any
    // other item, its size, date and location (12.1).
    private Task ShowDetailsAsync(FileRow? row)
    {
        ForgetPhotoPanel();
        if (row is null)
        {
            _host.Select(null, null);
            return Task.CompletedTask;
        }
        if (IsPhoto(row)) return ShowPhotoDetailsAsync(row);
        _host.Select(row.Entry.Path, FileDetails.For(row.Entry));
        return Task.CompletedTask;
    }

    // ── Activation and keys ───────────────────────────────────────────────

    // A double-click on one of the row's actions is two clicks on that
    // action, not a request to open the row.
    private void OnItemsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsInButton(e.OriginalSource as DependencyObject, (DependencyObject)sender)) return;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileRow row) Activate(row);
    }

    // Enter opens what is selected; Delete sends it to the Recycle Bin, as in
    // File Explorer, but Shift+Delete, which Explorer uses to delete for good,
    // does nothing here: this app never deletes for good. With several items
    // selected, Delete sends them all, and Enter opens none: File Explorer
    // would open each, and a key that starts a dozen applications at once is
    // not one to press by mistake. G and X mark a photo whose panel is shown,
    // as in the photo tool. Keys pressed on a row's action button are that
    // button's.
    private void OnItemsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is ButtonBase) return;
        List<FileRow> selected = SelectedRows();
        if (selected.Count == 0) return;
        bool modified = IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu);
        switch (e.Key)
        {
            case VirtualKey.Enter when selected.Count == 1:
                Activate(selected[0]);
                e.Handled = true;
                break;
            case VirtualKey.Delete when !modified && !IsDown(VirtualKey.Shift):
                _ = RecycleAsync(selected);
                e.Handled = true;
                break;
            case VirtualKey.K when !modified && _photoTile is not null:
                MarkPhoto(PhotoMark.Keep);
                e.Handled = true;
                break;
            case VirtualKey.X when !modified && _photoTile is not null:
                MarkPhoto(PhotoMark.Delete);
                e.Handled = true;
                break;
        }
    }

    private void Activate(FileRow row)
    {
        if (!row.Entry.IsFolder)
        {
            _ = OpenAsync(row);
            return;
        }
        if (_place is not null) _host.Open(_place.Enter(row.Entry.Path));
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool IsInButton(DependencyObject? element, DependencyObject stop)
    {
        for (DependencyObject? e = element; e is not null && !ReferenceEquals(e, stop); e = VisualTreeHelper.GetParent(e))
        {
            if (e is ButtonBase) return true;
        }
        return false;
    }

    private static void Label(FrameworkElement element, string key)
    {
        string text = FoldersText.Get(key);
        ToolTipService.SetToolTip(element, text);
        AutomationProperties.SetName(element, text);
    }
}

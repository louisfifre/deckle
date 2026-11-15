using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Deckle.Atelier.Views;

// The file tool's centre, in the display chosen at its bottom right: the
// list, the large rows or the gallery. A click selects an item and opens the
// details panel; a double-click or Enter on a folder enters it, in every
// display (spec, Behaviours: a click never opens the item). Opening a file is
// not built yet.
//
// The three displays show the same items (FileRow). Only the shown one holds
// them, so only its containers are realized: each is a ListView or a GridView,
// which realizes the items on screen alone, and asks the shell for an item's
// icon or picture when it realizes its container (FileRow.LoadAsync), off the
// UI thread behind the icon seam. A folder of thousands of items scrolls as
// one of ten in each display.
internal sealed partial class FilesView : UserControl, IAtelierToolView
{
    private readonly IAtelierToolHost _host;
    private FolderPlace? _place;
    private List<FileRow> _rows = [];

    // Set while the items are replaced or moved to another display, so the
    // selection changes a control makes by itself are not taken for the
    // person's.
    private bool _filling;

    public FilesView(IAtelierToolHost host)
    {
        _host = host;
        InitializeComponent();
        Label(DisplayListButton, "Display_List");
        Label(DisplayLargeRowsButton, "Display_LargeRows");
        Label(DisplayGalleryButton, "Display_Gallery");
        AutomationProperties.SetName(DisplayChoice, AtelierText.Get("Display_Group"));
        AutomationProperties.SetName(Rows, AtelierText.Get("Display_List"));
        AutomationProperties.SetName(LargeRows, AtelierText.Get("Display_LargeRows"));
        AutomationProperties.SetName(Gallery, AtelierText.Get("Display_Gallery"));
    }

    FrameworkElement IAtelierToolView.Content => this;

    // Kept by the view, which the window keeps for the tool's whole life: the
    // display stays as chosen from one folder to the next, as File Explorer
    // keeps the last one chosen (spec silent).
    internal FileListDisplay Display { get; private set; } = FileListDisplay.List;

    public async Task ShowAsync(FolderPlace place, string? selection, CancellationToken cancellation)
    {
        FolderListingResult listing = await Task.Run(() => FolderListing.List(place.Path), cancellation);
        if (cancellation.IsCancellationRequested) return;

        _place = place;
        foreach (FileRow old in _rows) old.Release(Display);
        List<FileRow> rows = [.. listing.Entries.Select(e => new FileRow(e))];
        _rows = rows;
        _filling = true;
        try
        {
            ListViewBase items = ItemsOf(Display);
            items.ItemsSource = rows;
            FileRow? selected = selection is null
                ? null
                : rows.FirstOrDefault(r => FolderPaths.Comparer.Equals(r.Entry.Path, selection));
            items.SelectedItem = selected;
            if (selected is not null) items.ScrollIntoView(selected);
            _host.Select(selected?.Entry.Path, selected is null ? null : FileDetails.For(selected.Entry));
        }
        finally
        {
            _filling = false;
        }

        string? message = listing.Failure switch
        {
            FolderListingFailure.None => rows.Count == 0 ? AtelierText.Get("Files_Empty") : null,
            FolderListingFailure.NotFound => AtelierText.Get("Files_NotFound"),
            FolderListingFailure.AccessDenied => AtelierText.Get("Files_AccessDenied"),
            _ => AtelierText.Get("Files_Unreadable"),
        };
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
    // their selection, scrolled to it, and the details panel is left as it
    // is, since what is selected has not changed. A click on the pressed
    // button would release it; it stays pressed instead, as one display is
    // always shown.
    internal void ShowDisplay(FileListDisplay display)
    {
        MarkDisplay(display);
        if (display == Display) return;

        ListViewBase from = ItemsOf(Display);
        ListViewBase to = ItemsOf(display);
        object? selected = from.SelectedItem;
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
            to.SelectedItem = selected;
            if (selected is not null) to.ScrollIntoView(selected);
        }
        finally
        {
            _filling = false;
        }

        Header.Visibility = display == FileListDisplay.List ? Visibility.Visible : Visibility.Collapsed;
        if (hadFocus) to.Focus(FocusState.Programmatic);
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

    // ── Selection and entering ────────────────────────────────────────────

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;
        if (((ListViewBase)sender).SelectedItem is FileRow row) _host.Select(row.Entry.Path, FileDetails.For(row.Entry));
        else _host.Select(null, null);
    }

    private void OnItemsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileRow row) Enter(row);
    }

    private void OnItemsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ((ListViewBase)sender).SelectedItem is FileRow row)
        {
            Enter(row);
            e.Handled = true;
        }
    }

    private void Enter(FileRow row)
    {
        if (!row.Entry.IsFolder || _place is null) return;
        _host.Open(_place.Enter(row.Entry.Path));
    }

    private static void Label(FrameworkElement element, string key)
    {
        string text = AtelierText.Get(key);
        ToolTipService.SetToolTip(element, text);
        AutomationProperties.SetName(element, text);
    }
}

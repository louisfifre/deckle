using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Deckle.Atelier.Views;

// The file tool's centre. A click selects a row and opens the details panel;
// a double-click or Enter on a folder enters it (spec, Behaviours: a click
// never opens the item). Opening a file is not built yet.
internal sealed partial class FilesView : UserControl, IAtelierToolView
{
    private readonly IAtelierToolHost _host;
    private FolderPlace? _place;

    // Set while the rows are replaced, so the selection changes the list makes
    // by itself are not taken for the person's.
    private bool _filling;

    public FilesView(IAtelierToolHost host)
    {
        _host = host;
        InitializeComponent();
        Label(DisplayListButton, "Display_List");
        Label(DisplayLargeRowsButton, "Display_LargeRows");
        Label(DisplayGalleryButton, "Display_Gallery");
        AutomationProperties.SetName(DisplayChoice, AtelierText.Get("Display_Group"));
    }

    FrameworkElement IAtelierToolView.Content => this;

    internal FileListDisplay Display { get; private set; } = FileListDisplay.List;

    public async Task ShowAsync(FolderPlace place, string? selection, CancellationToken cancellation)
    {
        FolderListingResult listing = await Task.Run(() => FolderListing.List(place.Path), cancellation);
        if (cancellation.IsCancellationRequested) return;

        _place = place;
        List<FileRow> rows = [.. listing.Entries.Select(e => new FileRow(e))];
        _filling = true;
        try
        {
            Rows.ItemsSource = rows;
            FileRow? selected = selection is null
                ? null
                : rows.FirstOrDefault(r => FolderPaths.Comparer.Equals(r.Entry.Path, selection));
            Rows.SelectedItem = selected;
            if (selected is not null) Rows.ScrollIntoView(selected);
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

        await LoadIconsAsync(rows, cancellation);
    }

    // After the rows are shown, so the list never waits for its icons. The
    // size asked for is the 16 DIP cell in physical pixels.
    private async Task LoadIconsAsync(List<FileRow> rows, CancellationToken cancellation)
    {
        int pixelSize = (int)Math.Round(IconCellSize * (XamlRoot?.RasterizationScale ?? 1.0));
        foreach (FileRow row in rows)
        {
            if (cancellation.IsCancellationRequested) return;
            row.Icon = await _host.Icons.GetAsync(row.Entry.Path, row.Entry.IsFolder, pixelSize, cancellation);
        }
    }

    private const double IconCellSize = 16;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;
        if (Rows.SelectedItem is FileRow row) _host.Select(row.Entry.Path, FileDetails.For(row.Entry));
        else _host.Select(null, null);
    }

    private void OnRowsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileRow row) Enter(row);
    }

    private void OnRowsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && Rows.SelectedItem is FileRow row)
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

    // Only the list is built; its button stays pressed.
    private void OnDisplayClick(object sender, RoutedEventArgs e)
    {
        Display = FileListDisplay.List;
        DisplayListButton.IsChecked = true;
    }

    private static void Label(FrameworkElement element, string key)
    {
        string text = AtelierText.Get(key);
        ToolTipService.SetToolTip(element, text);
        AutomationProperties.SetName(element, text);
    }
}

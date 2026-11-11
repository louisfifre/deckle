using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Deckle.Atelier.Views;

// The photo tool's centre (spec, Photos; boards 12.4, 12.5, 12.6, 12.22): the
// photos of the folder as a gallery, the counters of the marks and the
// commands above it. A click selects and fills the panel; a double-click or
// Enter shows the photo large; G and X mark (spec, Behaviours: a click never
// opens the item).
//
// Nothing here touches the disk on the UI thread: the folder is listed, the
// marks read and written, the capture data read and the marks applied off it.
// The gallery is a GridView over an ItemsWrapGrid, which realizes only the
// tiles on screen, so a folder of thousands of photos scrolls as one of ten.
//
// This file holds the gallery and the selection; the photo seen large, the
// application of the marks and darktable each have their partial file.
internal sealed partial class PhotosView : UserControl, IAtelierToolView
{
    private readonly IAtelierToolHost _host;
    private readonly string _moduleDirectory;
    private readonly PhotoMarkQueue _queue = new();
    private readonly Dictionary<string, CaptureReading> _readings = new(FolderPaths.Comparer);

    private FolderPlace? _place;
    private PhotoMarkStore? _store;
    private List<PhotoTile> _tiles = [];
    private PhotoDetails? _details;
    private CancellationTokenSource? _panelLoading;

    // Each ShowAsync takes a number; one that finds a newer number after an
    // await gives way, so a reload never paints over a newer folder.
    private int _showVersion;

    // Set while the tiles are replaced, so the selection changes the gallery
    // makes by itself are not taken for the person's.
    private bool _filling;

    public PhotosView(IAtelierToolHost host, string moduleDirectory)
    {
        _host = host;
        _moduleDirectory = moduleDirectory;
        InitializeComponent();
        Root.AddHandler(KeyDownEvent, new KeyEventHandler(OnRootKeyDown), handledEventsToo: true);
        ApplyLabels();
    }

    FrameworkElement IAtelierToolView.Content => this;

    public async Task ShowAsync(FolderPlace place, string? selection, CancellationToken cancellation)
    {
        int version = ++_showVersion;
        string folder = place.Path;

        // One step of the mark queue: it reads the store after any write
        // still pending for this folder, and builds the tiles off the UI thread.
        (PhotoFolderResult listing, PhotoMarkStore store, List<PhotoTile> tiles, DarktableInstall? darktable) =
            await _queue.Run(() =>
            {
                PhotoFolderResult l = PhotoFolder.List(folder);
                var s = new PhotoMarkStore(_moduleDirectory, folder);
                List<PhotoTile> t = [.. l.Photos.Select(p => new PhotoTile(p, s.MarkOf(p.Name)))];
                return (l, s, t, Darktable.Find());
            });
        if (cancellation.IsCancellationRequested || version != _showVersion) return;

        if (_place is null || !FolderPaths.Comparer.Equals(_place.Path, folder)) _readings.Clear();
        _place = place;
        _store = store;
        _tiles = tiles;
        _darktable = darktable;
        LeaveLarge(restoreFocus: false);

        _filling = true;
        try
        {
            Gallery.ItemsSource = tiles;
            PhotoTile? selected = selection is null
                ? null
                : tiles.FirstOrDefault(t => FolderPaths.Comparer.Equals(t.Photo.Path, selection));
            Gallery.SelectedItem = selected;
            if (selected is not null) Gallery.ScrollIntoView(selected);
        }
        finally
        {
            _filling = false;
        }

        string? message = listing.Failure switch
        {
            FolderListingFailure.None => tiles.Count == 0 ? AtelierText.Get("Photos_Empty") : null,
            FolderListingFailure.NotFound => AtelierText.Get("Files_NotFound"),
            FolderListingFailure.AccessDenied => AtelierText.Get("Files_AccessDenied"),
            _ => AtelierText.Get("Files_Unreadable"),
        };
        Message.Text = message ?? "";
        Message.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;

        ShowCounts();
        ShowDarktable(listing.Succeeded);
        await ShowPanelAsync();
    }

    // ── Counters and commands ─────────────────────────────────────────────

    private MarkCounts Counts()
    {
        int keep = 0, delete = 0;
        foreach (PhotoTile tile in _tiles)
        {
            if (tile.Mark == PhotoMark.Keep) keep++;
            else if (tile.Mark == PhotoMark.Delete) delete++;
        }
        return new MarkCounts(_tiles.Count - keep - delete, keep, delete);
    }

    // "215 sans marque · 160 à garder · 37 à supprimer" (12.4). What is left
    // to sort leads, in the stronger weight the board gives it. Applying is
    // the main command while there is something to delete; once nothing is,
    // darktable is (12.22, Open 21 drawn: one accent fill).
    private void ShowCounts()
    {
        MarkCounts counts = Counts();
        UnmarkedRun.Text = AtelierText.Format("Photos_Count_Unmarked_Format", counts.Unmarked);
        KeepRun.Text = AtelierText.Format("Photos_Count_Keep_Format", counts.Keep);
        DeleteRun.Text = AtelierText.Format("Photos_Count_Delete_Format", counts.Delete);
        Counters.Visibility = _tiles.Count == 0 || _applying ? Visibility.Collapsed : Visibility.Visible;

        // A folder with no photo has nothing to apply nor to hand to darktable:
        // the row is empty and the message below says why (Open 33).
        Commands.Visibility = _tiles.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        bool somethingToApply = counts.Delete > 0;
        ApplyButton.IsEnabled = somethingToApply && !_applying;
        ApplyButton.Style = ButtonStyle(somethingToApply ? "AccentButtonStyle" : "DefaultButtonStyle");
        DarktableButton.Style = ButtonStyle(somethingToApply ? "DefaultButtonStyle" : "AccentButtonStyle");
    }

    // ── Selection and the panel ───────────────────────────────────────────

    private IReadOnlyList<PhotoTile> SelectedTiles() =>
        [.. Gallery.SelectedItems.OfType<PhotoTile>()];

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;
        _ = ShowPanelAsync();
    }

    // The panel follows the selection. One photo waits for its capture data,
    // read off the UI thread once per photo and folder visit; several photos
    // show at once, since they show no capture data.
    private async Task ShowPanelAsync()
    {
        _panelLoading?.Cancel();
        var loading = new CancellationTokenSource();
        _panelLoading = loading;

        IReadOnlyList<PhotoTile> selected = SelectedTiles();
        if (selected.Count == 0)
        {
            _details = null;
            _host.Select(null, null);
            return;
        }
        if (selected.Count > 1)
        {
            _details = PhotoDetails.ForMany(selected, MarkSelection);
            _host.Select(((PhotoTile)Gallery.SelectedItem).Photo.Path, _details.Element);
            return;
        }

        PhotoTile tile = selected[0];
        if (!_readings.TryGetValue(tile.Photo.Path, out CaptureReading? reading))
        {
            string path = tile.Photo.Path;
            reading = await Task.Run(() => CaptureDataReader.Read(path));
            if (loading.IsCancellationRequested) return;
            _readings[path] = reading;
        }

        bool large = _largeTile is not null;
        _details = PhotoDetails.For(tile, reading, MarkSelection, large, _tiles.IndexOf(tile) + 1, _tiles.Count);
        _host.Select(tile.Photo.Path, _details.Element);
    }

    // ── Marks ─────────────────────────────────────────────────────────────

    // G, X or a button of the panel. Pressing the mark the photos already
    // carry takes it off, as pressing a pressed toggle releases it: the way
    // back to "sans marque" needs no third key. Marking changes nothing on
    // the disk but the app's own store (spec, Behaviours).
    private void MarkSelection(PhotoMark mark)
    {
        IReadOnlyList<PhotoTile> targets = _largeTile is { } large ? [large] : SelectedTiles();
        if (targets.Count == 0 || _applying) return;
        PhotoMark next = targets.All(t => t.Mark == mark) ? PhotoMark.None : mark;
        SetMark(targets, next);
    }

    private Task SetMark(IReadOnlyList<PhotoTile> tiles, PhotoMark mark)
    {
        foreach (PhotoTile tile in tiles) tile.Mark = mark;
        ShowSelectionMark();
        ShowCounts();

        if (_store is not { } store) return Task.CompletedTask;
        string[] names = [.. tiles.Select(t => t.Name)];
        return _queue.Run(() => store.Mark(names, mark));
    }

    // The panel's buttons show the mark of what the panel is about, whatever
    // was just marked: one mark when the selection shares it, else none.
    private void ShowSelectionMark()
    {
        if (_details is null) return;
        IReadOnlyList<PhotoTile> shown = _largeTile is { } large ? [large] : SelectedTiles();
        if (shown.Count == 0) return;
        PhotoMark first = shown[0].Mark;
        _details.ShowMark(shown.All(t => t.Mark == first) ? first : PhotoMark.None);
    }

    // ── Keyboard ──────────────────────────────────────────────────────────

    // Heard with the handled events too: the gallery and the buttons keep
    // their own keys, and these come on top of them. Without a modifier only,
    // so Ctrl+X stays what it is everywhere.
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            return;

        switch (e.Key)
        {
            case VirtualKey.G:
                MarkSelection(PhotoMark.Keep);
                e.Handled = true;
                break;
            case VirtualKey.X:
                MarkSelection(PhotoMark.Delete);
                e.Handled = true;
                break;
            case VirtualKey.Enter when _largeTile is null && Gallery.SelectedItems.Count == 1:
                OpenLarge((PhotoTile)Gallery.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Escape when _largeTile is not null:
                LeaveLarge(restoreFocus: true);
                e.Handled = true;
                break;
            case VirtualKey.Left when _largeTile is not null:
                MoveLarge(-1);
                e.Handled = true;
                break;
            case VirtualKey.Right when _largeTile is not null:
                MoveLarge(+1);
                e.Handled = true;
                break;
        }
    }

    private void OnGalleryDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is PhotoTile tile) OpenLarge(tile);
    }

    // ── Thumbnails ────────────────────────────────────────────────────────

    // A tile asks for its picture when its container is realized and lets it
    // go when the container is recycled (PhotoTile). The size is the tile's
    // in physical pixels.
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not PhotoTile tile) return;
        if (args.InRecycleQueue)
        {
            tile.ReleaseThumbnail();
            return;
        }
        _ = tile.LoadThumbnailAsync(_host.Icons, PixelSize(TileWidth));
    }

    private const double TileWidth = 184;

    private int PixelSize(double dips) => (int)Math.Round(dips * (XamlRoot?.RasterizationScale ?? 1.0));

    // ── Labels ────────────────────────────────────────────────────────────

    private void ApplyLabels()
    {
        ApplyButton.Content = AtelierText.Get("Photos_Apply");
        DarktableButton.Content = AtelierText.Get("Photos_Darktable");
        AutomationProperties.SetName(Gallery, AtelierText.Get("Photos_Gallery"));
        Label(LeaveLargeButton, "Photos_LeaveLarge");
        Label(PreviousButton, "Photos_Previous");
        Label(NextButton, "Photos_Next");
    }

    private static void Label(FrameworkElement element, string key)
    {
        string text = AtelierText.Get(key);
        ToolTipService.SetToolTip(element, text);
        AutomationProperties.SetName(element, text);
    }

    private static Style ButtonStyle(string key) => (Style)Application.Current.Resources[key];
}

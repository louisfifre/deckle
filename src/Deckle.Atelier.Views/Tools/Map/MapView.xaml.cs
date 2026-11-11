using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Atelier.Views;

// The map's centre: what can be used in the folder, as a flat details list
// (spec, Map; board 12.8b). Why flat and not by sections (12.8a): the spec's
// Open 11 draws both; Louis's one reaction, "avec l'option là, c'est top",
// points at the boxes per kind, and the board of the "always read" filter
// (12.8c) is drawn flat. The kinds stay told apart at a glance by their glyph
// and the Type column.
//
// A click selects a row and opens the panel (spec, Behaviours). The boxes
// filter the rows already read; ticking one never walks the disk again.
internal sealed partial class MapView : UserControl, IAtelierToolView
{
    private readonly IAtelierToolHost _host;
    private readonly Dictionary<MapItemKind, CheckBox> _kindBoxes = [];
    private readonly HashSet<MapItemKind> _hiddenKinds = [];
    private MapInventory? _map;

    // The selection the window keeps, so a filter that hides it and is then
    // undone finds it again.
    private string? _selection;

    // Set while the rows are replaced, so the selection changes the list makes
    // by itself are not taken for the person's.
    private bool _filling;

    // Counts the calls to ShowAsync, so only the latest one hides the bar.
    private int _showing;

    public MapView(IAtelierToolHost host)
    {
        _host = host;
        InitializeComponent();
        MapReview.Register(host, this);

        AutomationProperties.SetName(Filters, AtelierText.Get("MapFilters_Group"));
        ToolTipService.SetToolTip(TokensHeader, AtelierText.Get("Map_TokensHint"));
        EmptyGlyph.Glyph = AtelierGlyphs.MapTool;
        foreach (MapItemKind kind in MapInventory.KindsLookedFor)
        {
            var box = new CheckBox { Content = MapKindText.Plural(kind), IsChecked = true, MinWidth = 0, Tag = kind };
            box.Click += OnFilterClick;
            _kindBoxes[kind] = box;
            KindBoxes.Children.Add(box);
        }
    }

    FrameworkElement IAtelierToolView.Content => this;

    public async Task ShowAsync(FolderPlace place, string? selection, CancellationToken cancellation)
    {
        int call = ++_showing;
        Progress.Visibility = Visibility.Visible;
        MapInventory map;
        try
        {
            // The walk reads every file of the folder and below: off the UI thread.
            map = await Task.Run(() => MapInventory.Build(place.Path, cancellation), cancellation);
        }
        finally
        {
            if (call == _showing) Progress.Visibility = Visibility.Collapsed;
        }
        if (cancellation.IsCancellationRequested) return;

        _map = map;
        _selection = selection;
        EmptyTitle.Text = AtelierText.Format("Map_Empty_Format", place.Name);
        Show();
    }

    private void Show()
    {
        if (_map is not { } map) return;

        bool listed = map.Succeeded && !map.IsEmpty;
        EmptyState.Visibility = map.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        EmptyBody.Text = MapKindText.LookedFor();
        Filters.Visibility = Header.Visibility = Rows.Visibility = listed ? Visibility.Visible : Visibility.Collapsed;

        Truncated.IsOpen = map.IsTruncated;
        if (map.IsTruncated)
            Truncated.Message = AtelierText.Format("Map_Truncated_Format", MapKindText.Count(MapLimits.Default.MaxEntries));

        // Only the kinds the folder holds get a box: a box that filters
        // nothing is one more thing to read (spec, Intention).
        var present = map.Items.Select(i => i.Kind).ToHashSet();
        foreach ((MapItemKind kind, CheckBox box) in _kindBoxes)
            box.Visibility = present.Contains(kind) ? Visibility.Visible : Visibility.Collapsed;

        Fill(map);

        string? message = map.Failure switch
        {
            MapInventoryFailure.NotFound => AtelierText.Get("Map_NotFound"),
            MapInventoryFailure.AccessDenied => AtelierText.Get("Map_AccessDenied"),
            _ when listed && Rows.Items.Count == 0 => AtelierText.Get("Map_NoMatch"),
            _ => null,
        };
        Message.Text = message ?? "";
        Message.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // Board 12.8b sorts the flat list by name, as File Explorer does.
    private void Fill(MapInventory map)
    {
        var filter = new MapListFilter(AlwaysReadBox.IsChecked == true, _hiddenKinds);
        IReadOnlyList<MapItem> shown = filter.Apply(map.Items);
        var sharedNames = map.Items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<MapRow> rows = [.. shown
            .OrderBy(i => i.Name, FolderListing.NameOrder)
            .ThenBy(i => i.RelativePath, StringComparer.Ordinal)
            .Select(i => new MapRow(i, map.Neighbours(i).Count, sharedNames.Contains(i.Name)))];

        Total.Visibility = filter.AlwaysReadOnly && rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TotalLabel.Text = AtelierText.Format("Map_Total_Format", rows.Count);
        TotalTokens.Text = MapKindText.Count(MapListFilter.EstimatedTokensOf(shown));

        _filling = true;
        try
        {
            Rows.ItemsSource = rows;
            MapRow? selected = _selection is null ? null : rows.FirstOrDefault(r => Matches(map, r.Item, _selection));
            Rows.SelectedItem = selected;
            if (selected is not null) Rows.ScrollIntoView(selected);

            // A selection this tool cannot show (a photo chosen in the file
            // tool, a row the filter hides) stays the window's, but the panel
            // closes: it would describe something not on screen.
            _host.Select(_selection, selected is null ? null : MapDetails.For(map, selected.Item));
        }
        finally
        {
            _filling = false;
        }
    }

    // The path the window keeps for an item: its file, plus "#name" for a
    // server, which shares its file with the others (MapItem.Id).
    private static string SelectionOf(MapInventory map, MapItem item) =>
        System.IO.Path.Combine(map.Folder, item.Id.Replace('/', System.IO.Path.DirectorySeparatorChar));

    // A file chosen in another tool selects its item here too.
    private static bool Matches(MapInventory map, MapItem item, string selection) =>
        FolderPaths.Comparer.Equals(SelectionOf(map, item), selection)
        || (item.Kind != MapItemKind.McpServer && FolderPaths.Comparer.Equals(item.Path, selection));

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _map is not { } map) return;
        if (Rows.SelectedItem is MapRow row)
        {
            _selection = SelectionOf(map, row.Item);
            _host.Select(_selection, MapDetails.For(map, row.Item));
        }
        else
        {
            _selection = null;
            _host.Select(null, null);
        }
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        foreach ((MapItemKind kind, CheckBox box) in _kindBoxes)
        {
            if (box.IsChecked == true) _hiddenKinds.Remove(kind);
            else _hiddenKinds.Add(kind);
        }
        Show();
    }

    // The self-check's way to the state of board 12.8c.
    internal void SetAlwaysReadOnlyForReview(bool alwaysReadOnly)
    {
        AlwaysReadBox.IsChecked = alwaysReadOnly;
        OnFilterClick(AlwaysReadBox, new RoutedEventArgs());
    }
}

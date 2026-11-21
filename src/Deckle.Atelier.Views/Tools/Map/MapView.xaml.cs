using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Deckle.Core;
using Deckle.FolderMap;

namespace Deckle.Atelier.Views;

// The map's centre: what can be used in the folder, as a details list shown
// flat (board 12.8b) or by sections (12.8a), the spec's Open 11 drawing both.
// Flat is the default: Louis's one reaction, "avec l'option là, c'est top",
// points at the boxes per kind, and the board of the "always read" filter
// (12.8c) is drawn flat. Flat, the kinds are told apart by their glyph and the
// Type column; by sections, by a title per kind. The same filters and the same
// total serve both: only the order of the rows and the Type column change.
//
// A click selects a row and opens the panel (spec, Behaviours). The boxes
// filter the rows already read; ticking one never walks the disk again.
// The large graph takes the list's place under the same boxes
// (MapView.LargeGraph.cs).
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

    // Kept by the view, which the window keeps for the tool's whole life: the
    // display stays as chosen from one folder to the next for the session, as
    // the file tool keeps its own (spec silent). Not saved across starts.
    private bool _bySections;

    public MapView(IAtelierToolHost host)
    {
        _host = host;
        InitializeComponent();
        MapReview.Register(host, this);

        AutomationProperties.SetName(Filters, AtelierText.Get("MapFilters_Group"));
        AutomationProperties.SetName(DisplayChoice, AtelierText.Get("Map_Display_Group"));
        Label(FlatButton, "Map_Display_Flat");
        Label(SectionsButton, "Map_Display_Sections");
        AutomationProperties.SetName(DimensionChoice, AtelierText.Get("Map_LargeGraph_Dimension_Group"));
        LeaveLargeGraphText.Text = AtelierText.Get("Map_LargeGraph_Leave");
        ToolTipService.SetToolTip(LeaveLargeGraphButton, AtelierText.Get("Map_LargeGraph_Leave_Tip"));
        ToolTipService.SetToolTip(TokensHeader, AtelierText.Get("Map_TokensHint"));
        EmptyGlyph.Glyph = AtelierGlyphs.MapTool;
        foreach (MapItemKind kind in MapInventory.KindsLookedFor)
        {
            var box = new CheckBox { Content = MapKindText.Plural(kind), IsChecked = true, MinWidth = 0, Tag = kind };
            box.Click += OnFilterClick;
            _kindBoxes[kind] = box;
            Filters.Children.Add(box);
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
        Toolbar.Visibility = listed ? Visibility.Visible : Visibility.Collapsed;
        Header.Visibility = Rows.Visibility = listed && !_largeGraph ? Visibility.Visible : Visibility.Collapsed;
        LargeGraph.Visibility = listed && _largeGraph ? Visibility.Visible : Visibility.Collapsed;
        DisplayChoice.Visibility = _largeGraph ? Visibility.Collapsed : Visibility.Visible;
        LargeGraphCommands.Visibility = _largeGraph ? Visibility.Visible : Visibility.Collapsed;

        Truncated.IsOpen = map.IsTruncated;
        if (map.IsTruncated)
            Truncated.Message = AtelierText.Format("Map_Truncated_Format", MapKindText.Count(MapLimits.Default.MaxEntries));

        // Only the kinds the folder holds get a box: a box that filters
        // nothing is one more thing to read (spec, Intention).
        var present = map.Items.Select(i => i.Kind).ToHashSet();
        foreach ((MapItemKind kind, CheckBox box) in _kindBoxes)
            box.Visibility = present.Contains(kind) ? Visibility.Visible : Visibility.Collapsed;

        int shown = Fill(map);

        // A filter that leaves no row (spec, Open 33): one line in the middle
        // of the list, as File Explorer says a search found nothing; the
        // header and the boxes stay, so the filter can be undone where it was set.
        string? message = map.Failure switch
        {
            MapInventoryFailure.NotFound => AtelierText.Get("Map_NotFound"),
            MapInventoryFailure.AccessDenied => AtelierText.Get("Map_AccessDenied"),
            _ when listed && shown == 0 => AtelierText.Get("Map_NoMatch"),
            _ => null,
        };
        Message.Text = message ?? "";
        Message.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // Fills the list with the rows the boxes keep, in the display chosen, and
    // answers how many there are. Both displays sort by name, as File Explorer
    // does (MapListOrder); by sections, a kind left without a row has no title.
    private int Fill(MapInventory map)
    {
        MapListFilter filter = CurrentFilter();
        IReadOnlyList<MapItem> shown = filter.Apply(map.Items);
        var sharedNames = map.Items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        MapRow RowOf(MapItem i) => new(i, map.Neighbours(i).Count, sharedNames.Contains(i.Name), kindShown: !_bySections);

        List<MapRow> rows;
        object source;
        if (_bySections)
        {
            List<MapRowGroup> groups = [.. MapListOrder.Sections(shown).Select(s => new MapRowGroup(s, s.Items.Select(RowOf)))];
            rows = [.. groups.SelectMany(g => g)];
            source = new CollectionViewSource { IsSourceGrouped = true, Source = groups }.View;
        }
        else
        {
            rows = [.. MapListOrder.Flat(shown).Select(RowOf)];
            source = rows;
        }

        HeaderKindColumn.Width = _bySections ? new GridLength(0) : MapRow.MapKindColumnWidth;
        HeaderKind.Visibility = _bySections ? Visibility.Collapsed : Visibility.Visible;
        FlatButton.IsChecked = !_bySections;
        SectionsButton.IsChecked = _bySections;

        Total.Visibility = filter.AlwaysReadOnly && rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TotalLabel.Text = AtelierText.Format("Map_Total_Format", rows.Count);
        TotalTokens.Text = MapKindText.Count(MapListFilter.EstimatedTokensOf(shown));

        MapRow? selected;
        _filling = true;
        try
        {
            Rows.ItemsSource = source;
            selected = _selection is null ? null : rows.FirstOrDefault(r => Matches(map, r.Item, _selection));
            Rows.SelectedItem = selected;
            if (selected is not null) Rows.ScrollIntoView(selected);

            // A selection this tool cannot show (a photo chosen in the file
            // tool, a row the filter hides) stays the window's, but the panel
            // closes: it would describe something not on screen.
            _host.Select(_selection, selected is null ? null : DetailsOf(map, selected.Item));
        }
        finally
        {
            _filling = false;
        }
        if (_largeGraph) ShowLargeGraph(map, filter, selected?.Item);
        return rows.Count;
    }

    // The boxes above the list, which the large graph shares: one state, so
    // a box ticked in one is ticked in the other.
    private MapListFilter CurrentFilter() => new(AlwaysReadBox.IsChecked == true, _hiddenKinds);

    // The panel of an item; its graph's enlarge button opens the large graph,
    // and goes while the large graph is open.
    private UIElement DetailsOf(MapInventory map, MapItem item) =>
        MapDetails.For(map, item, _largeGraph ? null : EnterLargeGraph);

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
            _host.Select(_selection, DetailsOf(map, row.Item));
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

    // The same rows in the other display, with their selection. A click on
    // the pressed button would release it; it stays pressed instead, as one
    // display is always shown (the file tool's rule).
    private void OnDisplayClick(object sender, RoutedEventArgs e) => ShowBySections(ReferenceEquals(sender, SectionsButton));

    private void ShowBySections(bool bySections)
    {
        bool hadFocus = Rows.FocusState != FocusState.Unfocused;
        _bySections = bySections;
        Show();
        if (hadFocus) Rows.Focus(FocusState.Programmatic);
    }

    // A tooltip and a name for a button that shows only a glyph.
    private static void Label(FrameworkElement button, string key)
    {
        string text = AtelierText.Get(key);
        ToolTipService.SetToolTip(button, text);
        AutomationProperties.SetName(button, text);
    }

    // The self-check's ways to the states of boards 12.8a and 12.8c, and to
    // a filter that leaves no row.
    internal void SetAlwaysReadOnlyForReview(bool alwaysReadOnly)
    {
        AlwaysReadBox.IsChecked = alwaysReadOnly;
        OnFilterClick(AlwaysReadBox, new RoutedEventArgs());
    }

    internal void SetKindShownForReview(MapItemKind kind, bool shown)
    {
        _kindBoxes[kind].IsChecked = shown;
        OnFilterClick(_kindBoxes[kind], new RoutedEventArgs());
    }

    internal void SetBySectionsForReview(bool bySections) => ShowBySections(bySections);
}

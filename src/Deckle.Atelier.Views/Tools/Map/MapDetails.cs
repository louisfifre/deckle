using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

// What the details panel holds for an item of the map (board 12.8a): what it
// is and where, its details, then its links apart from them (spec, Details
// panel: "For an item that has links, the panel separates its details from
// its links"): the small graph, then "Cite" and "Est cité par", which say in
// words what the graph shows. The details come from DetailsSheet so the
// panel opens like every other tool's; the graph and the lists are sections
// drawn the same way (MapPanelSection).
internal static class MapDetails
{
    public static UIElement For(MapInventory map, MapItem item)
    {
        var root = new StackPanel();
        root.Children.Add(Sheet(item).Element);

        MapNeighbourhood around = MapNeighbourhood.Around(map, item);
        if (around.Total == 0)
        {
            // Open 33, "a map item with no link": a graph of one node says
            // nothing a sentence does not, so only the sentence stays.
            root.Children.Add(MapPanelSection.Create(AtelierText.Get("Map_Links_Section"), Secondary(AtelierText.Get("Map_NoLinks"))));
            return root;
        }

        root.Children.Add(MapPanelSection.Create(AtelierText.Get("Map_Graph_Section"), new MapLinkGraph(around)));
        var lists = new StackPanel { Spacing = 12 };
        lists.Children.Add(LinkList("Map_Cites_Format", map, map.LinksFrom(item).Select(l => (map.Find(l.ToId)!, map.IsMutual(l)))));
        lists.Children.Add(LinkList("Map_CitedBy_Format", map, map.LinksTo(item).Select(l => (map.Find(l.FromId)!, map.IsMutual(l)))));
        root.Children.Add(MapPanelSection.Create(AtelierText.Get("Map_Links_Section"), lists));
        return root;
    }

    private static DetailsSheet Sheet(MapItem item)
    {
        string? tokens = item.EstimatedTokens is int t ? AtelierText.Format("Map_Tokens_Format", MapKindText.Count(t)) : null;

        // A skill or a subagent puts only its name and description in the
        // context at start; said apart, since the box keeps only what loads whole.
        string? startup = !item.IsAlwaysRead && item.EstimatedStartupTokens is int s
            ? AtelierText.Format("Map_Startup_Format", MapKindText.Count(s))
            : null;
        string? location = item.DeclaredAtLine is int line
            ? AtelierText.Format("Map_Location_Line_Format", item.RelativePath, line)
            : null;
        string summaryLabel = AtelierText.Get(item.Kind == MapItemKind.McpServer ? "Map_Details_Command" : "Map_Details_Description");

        return new DetailsSheet($"{MapKindText.Name(item.Kind)} · {MapKindText.Place(item)}", item.Name)
            .Section(
                AtelierText.Get("Map_Details_Section"),
                (summaryLabel, item.Summary),
                (AtelierText.Get("Map_Details_Loading"), MapKindText.Loading(item.Loading)),
                (AtelierText.Get("Map_Details_ReadBy"), MapKindText.Readers(item.ReadAtStartBy)),
                (AtelierText.Get("Map_Details_Startup"), startup),
                (AtelierText.Get("Map_Details_Lines"), item.Lines is int n ? MapKindText.Count(n) : null),
                (AtelierText.Get("Map_Details_Tokens"), tokens),
                (AtelierText.Get("Map_Details_Modified"), EntryFormat.Date(item.Modified, AtelierText.Culture)),
                (AtelierText.Get("Map_Details_Location"), location));
    }

    // "Cite (4)" then one line per item, by name; a link both ways says so
    // after the name, in secondary text (12.8a). Plain text (Open 23).
    private static StackPanel LinkList(string titleKey, MapInventory map, IEnumerable<(MapItem Item, bool Mutual)> links)
    {
        List<(MapItem Item, bool Mutual)> ordered = [.. links
            .OrderBy(l => l.Item.Name, FolderListing.NameOrder)
            .ThenBy(l => l.Item.RelativePath, StringComparer.Ordinal)];

        var list = new StackPanel { Spacing = 4 };
        list.Children.Add(new TextBlock
        {
            Text = AtelierText.Format(titleKey, ordered.Count),
            Style = Resource<Style>("CaptionTextBlockStyle"),
            FontWeight = FontWeights.SemiBold,
        });
        if (ordered.Count == 0)
        {
            list.Children.Add(Secondary(AtelierText.Get("Map_None")));
            return list;
        }
        foreach ((MapItem item, bool mutual) in ordered)
        {
            var line = new TextBlock
            {
                Style = Resource<Style>("CaptionTextBlockStyle"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };
            line.Inlines.Add(new Run { Text = item.Name });
            if (mutual)
                line.Inlines.Add(new Run
                {
                    Text = " · " + AtelierText.Get("Map_Mutual"),
                    Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
                });
            ToolTipService.SetToolTip(line, MapKindText.Place(item));
            list.Children.Add(line);
        }
        return list;
    }

    private static TextBlock Secondary(string text) => new()
    {
        Text = text,
        Style = Resource<Style>("CaptionTextBlockStyle"),
        Foreground = Resource<Brush>("TextFillColorSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap,
    };

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}

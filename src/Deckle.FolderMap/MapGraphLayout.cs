namespace Deckle.FolderMap;

// One item drawn in the large graph: the column of its kind, its row in that
// column counted from the top, and whether it stands out. With an item
// chosen, the item and its neighbours stand out and the rest is dimmed
// (board 12.9); with none chosen, nothing is dimmed.
public sealed record MapGraphNode(MapItem Item, int Column, int Row, bool IsChosen, bool StandsOut);

// One link between two drawn items, once per pair: BothWays when each cites
// the other, else From cites To. It stands out when it touches the chosen item.
public sealed record MapGraphEdge(MapItem From, MapItem To, bool BothWays, bool StandsOut);

// One column of the large graph: one kind, how many of its items are drawn,
// and how many the filter kept but the graph leaves out ("+N").
public sealed record MapGraphColumn(MapItemKind Kind, int Drawn, int Hidden);

// Where the large graph puts each item (spec, Map; board 12.9). Deliberately
// plain, and the same for the same inventory, filter and choice: one column
// per kind, in the order of KindsLookedFor, each column by name the way
// File Explorer sorts. Louis said on 2026-11-15 that the drawing itself is
// decided later; this is the placeholder that lets what surrounds it exist.
//
// Only what the filter keeps is drawn, and only links between drawn items.
// A column holds at most maxRows rows. When a kind has more items than that,
// names would stop being readable: with an item chosen, the graph draws only
// the item and its neighbours; with none, the first items by name. Either
// way, a column that leaves items out keeps its last row for "+N".
public sealed record MapGraphLayout(
    IReadOnlyList<MapGraphColumn> Columns,
    IReadOnlyList<MapGraphNode> Nodes,
    IReadOnlyList<MapGraphEdge> Edges,
    bool IsNeighbourhood)
{
    // The rows the tallest column takes, "+N" included.
    public int RowCount => Columns.Count == 0 ? 0 : Columns.Max(c => c.Drawn + (c.Hidden > 0 ? 1 : 0));

    public int Hidden => Columns.Sum(c => c.Hidden);

    public static MapGraphLayout Of(MapInventory map, MapListFilter filter, MapItem? chosen, int maxRows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        IReadOnlyList<MapItem> kept = filter.Apply(map.Items);
        if (chosen is not null && !kept.Any(i => SameId(i, chosen))) chosen = null;

        IReadOnlyList<MapSection> sections = MapListOrder.Sections(kept);
        bool fits = sections.All(s => s.Count <= maxRows);
        bool neighbourhood = !fits && chosen is not null;

        // The items worth keeping when a column is too tall, best first: the
        // chosen item, then its neighbours in the panel's order (both ways,
        // cited, citing, by name).
        Dictionary<string, int> rank = new(StringComparer.OrdinalIgnoreCase);
        if (chosen is not null)
        {
            rank[chosen.Id] = 0;
            MapNeighbourhood around = MapNeighbourhood.Around(map, chosen, int.MaxValue);
            for (int i = 0; i < around.Shown.Count; i++) rank[around.Shown[i].Item.Id] = i + 1;
        }

        var columns = new List<MapGraphColumn>();
        var nodes = new List<MapGraphNode>();
        var drawn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MapSection section in sections)
        {
            // With an item chosen, every column keeps only the neighbourhood,
            // so the graph reads as one view of it; with none, only a column
            // too tall is cut.
            IReadOnlyList<MapItem> items = section.Items;
            int room = Math.Max(1, maxRows - 1);
            if (neighbourhood)
            {
                HashSet<MapItem> near = [.. items.Where(i => rank.ContainsKey(i.Id)).OrderBy(i => rank[i.Id]).Take(room)];
                items = [.. items.Where(near.Contains)];
            }
            else if (items.Count > maxRows)
            {
                items = [.. items.Take(room)];
            }

            int column = columns.Count;
            for (int row = 0; row < items.Count; row++)
            {
                MapItem item = items[row];
                bool isChosen = chosen is not null && SameId(item, chosen);
                bool standsOut = chosen is null || rank.ContainsKey(item.Id);
                nodes.Add(new MapGraphNode(item, column, row, isChosen, standsOut));
                drawn.Add(item.Id);
            }
            columns.Add(new MapGraphColumn(section.Kind, items.Count, section.Count - items.Count));
        }

        var edges = new List<MapGraphEdge>();
        var seen = new HashSet<(string, string)>();
        foreach (MapLink link in map.Links)
        {
            if (!drawn.Contains(link.FromId) || !drawn.Contains(link.ToId)) continue;
            string a = link.FromId.ToUpperInvariant(), b = link.ToId.ToUpperInvariant();
            if (!seen.Add(string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a))) continue;

            MapItem from = map.Find(link.FromId)!, to = map.Find(link.ToId)!;
            bool standsOut = chosen is null || SameId(from, chosen) || SameId(to, chosen);
            edges.Add(new MapGraphEdge(from, to, map.IsMutual(link), standsOut));
        }

        return new MapGraphLayout(columns, nodes, edges, neighbourhood);
    }

    private static bool SameId(MapItem a, MapItem b) => string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
}

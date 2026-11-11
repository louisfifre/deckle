namespace Deckle.Atelier;

// Which way a link runs, seen from the item at the centre of the panel's graph.
public enum MapLinkDirection
{
    // The centre cites the neighbour.
    Out,

    // The neighbour cites the centre.
    In,

    // Each cites the other: "dans les deux sens" (12.8a).
    Both,
}

public sealed record MapNeighbour(MapItem Item, MapLinkDirection Direction);

// What the panel's small graph draws around one item (spec, Map: "Beyond a
// few links it shows +N"; board 12.8a draws four, then "+1"). The neighbours
// shown come in a fixed order so two selections of the same item draw the
// same graph: the links both ways first, since they say the most about the
// item, then what it cites, then what cites it, each by name.
public sealed record MapNeighbourhood(MapItem Centre, IReadOnlyList<MapNeighbour> Shown, int Hidden)
{
    public const int MaxShown = 4;

    public int Total => Shown.Count + Hidden;

    public static MapNeighbourhood Around(MapInventory map, MapItem centre, int maxShown = MaxShown)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxShown);
        var cited = new HashSet<string>(map.LinksFrom(centre).Select(l => l.ToId), StringComparer.OrdinalIgnoreCase);
        var citing = new HashSet<string>(map.LinksTo(centre).Select(l => l.FromId), StringComparer.OrdinalIgnoreCase);

        List<MapNeighbour> all = [.. map.Neighbours(centre)
            .Select(n => new MapNeighbour(n, (cited.Contains(n.Id), citing.Contains(n.Id)) switch
            {
                (true, true) => MapLinkDirection.Both,
                (true, false) => MapLinkDirection.Out,
                _ => MapLinkDirection.In,
            }))
            .OrderBy(n => n.Direction switch { MapLinkDirection.Both => 0, MapLinkDirection.Out => 1, _ => 2 })
            .ThenBy(n => n.Item.Name, FolderListing.NameOrder)
            .ThenBy(n => n.Item.RelativePath, StringComparer.Ordinal)];

        return new MapNeighbourhood(centre, all.Take(maxShown).ToList(), Math.Max(0, all.Count - maxShown));
    }
}

namespace Deckle.Atelier;

public enum MapInventoryFailure
{
    None,
    NotFound,
    AccessDenied,
}

// What the map found in one folder: the items, the links between them, and
// what it did not look at. Built by a script, without a model: Build walks
// the disk, so callers run it off the UI thread and pass a token to stop it.
public sealed class MapInventory
{
    private readonly Dictionary<string, MapItem> _byId;
    private readonly ILookup<string, MapLink> _from;
    private readonly ILookup<string, MapLink> _to;
    private readonly HashSet<(string, string)> _pairs;

    internal MapInventory(
        string folder,
        IReadOnlyList<MapItem> items,
        IReadOnlyList<MapLink> links,
        IReadOnlyList<MapSkip> skipped,
        bool truncated,
        MapInventoryFailure failure)
    {
        Folder = folder;
        Items = items;
        Links = links;
        Skipped = skipped;
        IsTruncated = truncated;
        Failure = failure;
        _byId = items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        _from = links.ToLookup(l => l.FromId, StringComparer.OrdinalIgnoreCase);
        _to = links.ToLookup(l => l.ToId, StringComparer.OrdinalIgnoreCase);
        _pairs = [.. links.Select(l => (l.FromId.ToUpperInvariant(), l.ToId.ToUpperInvariant()))];
    }

    // What the map looks for, in the order of the sections: the empty state
    // lists it (12.14, "Ni fichier de contexte, ni skill, ...").
    public static IReadOnlyList<MapItemKind> KindsLookedFor { get; } = Enum.GetValues<MapItemKind>();

    public string Folder { get; }

    // Sorted by kind in the order of KindsLookedFor, then by name the way
    // File Explorer sorts.
    public IReadOnlyList<MapItem> Items { get; }

    public IReadOnlyList<MapLink> Links { get; }

    // The folders left out (.git, node_modules, bin, ...) and the files not
    // read, each with its reason.
    public IReadOnlyList<MapSkip> Skipped { get; }

    // True when the walk stopped at MapLimits.MaxEntries: the inventory is partial.
    public bool IsTruncated { get; }

    public MapInventoryFailure Failure { get; }

    public bool Succeeded => Failure == MapInventoryFailure.None;

    // A readable folder with nothing of the kinds the map looks for (12.14).
    public bool IsEmpty => Succeeded && Items.Count == 0;

    // "Total toujours lu" (12.8c): the items every session loads, summed.
    public int EstimatedAlwaysReadTokens => Items.Where(i => i.IsAlwaysRead).Sum(i => i.EstimatedTokens ?? 0);

    // Everything loaded at session start: the always-read files plus the
    // names and descriptions of skills and subagents.
    public int EstimatedStartupTokens => Items.Sum(i => i.EstimatedStartupTokens ?? 0);

    public static MapInventory Build(string folder, CancellationToken cancellation = default, MapLimits? limits = null) =>
        MapInventoryBuilder.Build(folder, limits ?? MapLimits.Default, cancellation);

    public MapItem? Find(string id) => _byId.GetValueOrDefault(id);

    // "Cite (n)": what the item links to.
    public IReadOnlyList<MapLink> LinksFrom(MapItem item) => [.. _from[item.Id]];

    // "Est cité par (n)": what links to the item.
    public IReadOnlyList<MapLink> LinksTo(MapItem item) => [.. _to[item.Id]];

    // "dans les deux sens": the other item links back.
    public bool IsMutual(MapLink link) => _pairs.Contains((link.ToId.ToUpperInvariant(), link.FromId.ToUpperInvariant()));

    // The items linked to this one in either direction, each once: the
    // "Liens" column (12.8a) and the nodes of the panel's graph.
    public IReadOnlyList<MapItem> Neighbours(MapItem item) =>
        [.. _from[item.Id].Select(l => l.ToId).Concat(_to[item.Id].Select(l => l.FromId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => _byId[id])];

    internal static MapInventory Failed(string folder, MapInventoryFailure failure) => new(folder, [], [], [], false, failure);
}

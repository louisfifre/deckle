using Deckle.Core;

namespace Deckle.FolderMap;

// The two displays of the map's list, from the rows a filter kept (12.8a,
// 12.8b; spec, Open 11: both drawn). Both sort by name the way File Explorer
// sorts, then by path so two items of one name keep a fixed order.
public static class MapListOrder
{
    // Flat: every row in one list (12.8b).
    public static IReadOnlyList<MapItem> Flat(IEnumerable<MapItem> shown) =>
        [.. ByName(shown)];

    // By sections: one group per kind, in the order of KindsLookedFor. A kind
    // the filter left without a row gets no section, so no title stands over
    // nothing.
    public static IReadOnlyList<MapSection> Sections(IEnumerable<MapItem> shown)
    {
        ILookup<MapItemKind, MapItem> byKind = shown.ToLookup(i => i.Kind);
        return [.. MapInventory.KindsLookedFor
            .Where(byKind.Contains)
            .Select(kind => new MapSection(kind, [.. ByName(byKind[kind])]))];
    }

    private static IEnumerable<MapItem> ByName(IEnumerable<MapItem> items) =>
        items.OrderBy(i => i.Name, FolderNameOrder.Comparer).ThenBy(i => i.RelativePath, StringComparer.Ordinal);
}

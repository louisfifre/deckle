namespace Deckle.FolderMap;

// One titled group of the map's list shown by sections (12.8a): the items of
// one kind, already in their order. Its title carries Count beside the kind's
// plural name.
public sealed record MapSection(MapItemKind Kind, IReadOnlyList<MapItem> Items)
{
    public int Count => Items.Count;
}

using Deckle.FolderMap;

namespace Deckle.Folders.Views;

// One section of the map's list shown by sections (12.8a): its rows, and the
// title above them, the kind's plural name followed by its count, the count
// in secondary text as the board draws it ("Skills  3").
internal sealed class MapRowGroup : List<MapRow>
{
    public MapRowGroup(MapSection section, IEnumerable<MapRow> rows) : base(rows)
    {
        Title = MapKindText.Plural(section.Kind);
        CountText = MapKindText.Count(section.Count);
    }

    public string Title { get; }
    public string CountText { get; }
}

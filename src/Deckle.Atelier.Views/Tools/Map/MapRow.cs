using Microsoft.UI.Xaml;

namespace Deckle.Atelier.Views;

// One row of the map's list, its texts formatted once when the folder is read.
// Columns of board 12.8b: name, type, links, lines, tokens, date modified. A
// figure the item does not have (a server's lines, a file too large to read)
// stays blank, as File Explorer leaves a folder's size blank.
internal sealed class MapRow
{
    public MapRow(MapItem item, int links, bool nameIsShared)
    {
        Item = item;
        Glyph = MapKindText.Glyph(item.Kind);
        Kind = MapKindText.Name(item.Kind);
        Links = MapKindText.Count(links);
        Lines = item.Lines is int lines ? MapKindText.Count(lines) : "";
        Tokens = item.EstimatedTokens is int tokens ? MapKindText.Count(tokens) : "";
        Modified = EntryFormat.Date(item.Modified, AtelierText.Culture);
        Place = MapKindText.Place(item);

        // Two items of one name (a CLAUDE.md at the root and one in src) are
        // told apart by their folder, shown only then, so the list stays as
        // plain as the boards draw it.
        string folder = System.IO.Path.GetDirectoryName(Place)?.Replace('\\', '/') ?? "";
        Folder = nameIsShared ? folder : "";
        FolderVisibility = nameIsShared && folder.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public MapItem Item { get; }
    public string Name => Item.Name;
    public string Glyph { get; }
    public string Kind { get; }
    public string Links { get; }
    public string Lines { get; }
    public string Tokens { get; }
    public string Modified { get; }
    public string Place { get; }
    public string Folder { get; }
    public Visibility FolderVisibility { get; }
}

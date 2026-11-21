namespace Deckle.Folders.Views;

// The map tool, name not chosen (spec, Map, Open 10: drawn "Map"; boards
// 12.8b, 12.8c, 12.14). What can be used in the folder, as a details list,
// with the links of the selected item in the panel. The large graph (12.9)
// and the 3D graph (12.10) are not built, and nothing leads to them yet.
internal sealed class MapTool : IFoldersTool
{
    public string Id => "map";
    public string Name => FoldersText.Get("Tool_Map");
    public string Glyph => FoldersGlyphs.MapTool;

    public IFoldersToolView CreateView(IFoldersToolHost host) => new MapView(host);
}

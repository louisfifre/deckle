namespace Deckle.Folders.Views;

// The sizes tool (spec, Sizes; boards 12.7, 12.21, 12.23): what every folder
// beneath the place weighs, as a list with bars, and the heaviest files.
internal sealed class SizesTool : IFoldersTool
{
    public string Id => "sizes";
    public string Name => FoldersText.Get("Tool_Sizes");
    public string Glyph => FoldersGlyphs.SizesTool;

    public IFoldersToolView CreateView(IFoldersToolHost host) => new SizesView(host);
}

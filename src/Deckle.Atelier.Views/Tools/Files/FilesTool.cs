namespace Deckle.Atelier.Views;

// The file tool: the plain view of a folder, a details list with a header row
// (spec, File list; boards 12.1, 12.18, 12.20).
internal sealed class FilesTool : IAtelierTool
{
    public string Id => "files";
    public string Name => AtelierText.Get("Tool_Files");
    public string Glyph => AtelierGlyphs.FilesTool;

    public IAtelierToolView CreateView(IAtelierToolHost host) => new FilesView(host);
}

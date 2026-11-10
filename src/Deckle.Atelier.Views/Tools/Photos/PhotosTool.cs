namespace Deckle.Atelier.Views;

// The photo tool (spec, Photos; boards 12.4, 12.5, 12.6, 12.19, 12.22, 12.24).
// A placeholder until its own session fills this folder.
internal sealed class PhotosTool : IAtelierTool
{
    public string Id => "photos";
    public string Name => AtelierText.Get("Tool_Photos");
    public string Glyph => AtelierGlyphs.PhotosTool;

    public IAtelierToolView CreateView(IAtelierToolHost host) => new ToolPlaceholderView(this);
}

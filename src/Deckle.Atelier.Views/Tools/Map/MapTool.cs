namespace Deckle.Atelier.Views;

// The map tool, name not chosen (spec, Map, Open 10; boards 12.8a, 12.8b,
// 12.8c, 12.9, 12.14). A placeholder until its own session fills this folder.
internal sealed class MapTool : IAtelierTool
{
    public string Id => "map";
    public string Name => AtelierText.Get("Tool_Map");
    public string Glyph => AtelierGlyphs.MapTool;

    public IAtelierToolView CreateView(IAtelierToolHost host) => new ToolPlaceholderView(this);
}

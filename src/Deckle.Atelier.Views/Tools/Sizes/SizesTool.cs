namespace Deckle.Atelier.Views;

// The sizes tool (spec, Sizes; boards 12.7, 12.21, 12.23): what every folder
// beneath the place weighs, as a list with bars, and the heaviest files.
internal sealed class SizesTool : IAtelierTool
{
    public string Id => "sizes";
    public string Name => AtelierText.Get("Tool_Sizes");
    public string Glyph => AtelierGlyphs.SizesTool;

    public IAtelierToolView CreateView(IAtelierToolHost host) => new SizesView(host);
}

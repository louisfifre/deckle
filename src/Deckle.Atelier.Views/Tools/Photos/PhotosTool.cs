using Deckle.Core;

namespace Deckle.Atelier.Views;

// The photo tool (spec, Photos; boards 12.4, 12.5, 12.6, 12.22): the photos of
// the folder as a gallery, their marks, the application of the marks and the
// way to darktable.
//
// The marks live in the module's own data folder, the one the window keeps
// its added folders in. The tool seam hands no folder to a tool, so the tool
// asks AppPaths for the module's, by the id the app registers the module
// under (Deckle.Modules.ModuleIds.Atelier, which this library does not
// reference): the same folder, by the same rule.
internal sealed class PhotosTool : IAtelierTool
{
    private const string ModuleId = "atelier";

    public string Id => "photos";
    public string Name => AtelierText.Get("Tool_Photos");
    public string Glyph => AtelierGlyphs.PhotosTool;

    public IAtelierToolView CreateView(IAtelierToolHost host) =>
        new PhotosView(host, AppPaths.GetModuleDirectory(ModuleId));
}

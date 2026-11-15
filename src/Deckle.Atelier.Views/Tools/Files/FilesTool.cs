namespace Deckle.Atelier.Views;

// The file tool: the plain view of a folder, a details list with a header row
// (spec, File list; boards 12.1, 12.18, 12.20). A photo selected in it shows
// the photo tool's panel, on the photo tool's marks, so it takes the module's
// data folder and the queue the two tools share (AtelierToolCatalog).
internal sealed class FilesTool(string moduleDirectory, PhotoMarkQueue marks) : IAtelierTool
{
    public string Id => "files";
    public string Name => AtelierText.Get("Tool_Files");
    public string Glyph => AtelierGlyphs.FilesTool;

    public IAtelierToolView CreateView(IAtelierToolHost host) =>
        new FilesView(host, moduleDirectory, marks, new ShellRecycleBin(), new ShellFileLauncher());
}

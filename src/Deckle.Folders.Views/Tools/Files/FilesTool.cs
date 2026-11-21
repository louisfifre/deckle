namespace Deckle.Folders.Views;

// The file tool: the plain view of a folder, a details list with a header row
// (spec, File list; boards 12.1, 12.18, 12.20). A photo selected in it shows
// the photo tool's panel, on the photo tool's marks, so it takes the module's
// data folder and the queue the two tools share (FoldersToolCatalog).
internal sealed class FilesTool(string moduleDirectory, PhotoMarkQueue marks, PhotoDescriptions? descriptions) : IFoldersTool
{
    public string Id => "files";
    public string Name => FoldersText.Get("Tool_Files");
    public string Glyph => FoldersGlyphs.FilesTool;

    public IFoldersToolView CreateView(IFoldersToolHost host) =>
        new FilesView(host, moduleDirectory, marks, new ShellRecycleBin(), new ShellFileLauncher(), descriptions);
}

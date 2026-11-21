namespace Deckle.Folders.Views;

// The photo tool (spec, Photos; boards 12.4, 12.5, 12.6, 12.22): the photos of
// the folder as a gallery, their marks, the application of the marks and the
// way to darktable.
//
// The marks live in the module's own data folder, the one the window keeps
// its added folders in, and go through the queue the file tool shares, since
// a photo selected there shows and sets the same marks (FoldersToolCatalog).
// The descriptions are the app's, shared with the file tool too; null when
// the app has no model to ask.
internal sealed class PhotosTool(string moduleDirectory, PhotoMarkQueue marks, PhotoDescriptions? descriptions) : IFoldersTool
{
    public string Id => "photos";
    public string Name => FoldersText.Get("Tool_Photos");
    public string Glyph => FoldersGlyphs.PhotosTool;

    public IFoldersToolView CreateView(IFoldersToolHost host) => new PhotosView(host, moduleDirectory, marks, descriptions);
}

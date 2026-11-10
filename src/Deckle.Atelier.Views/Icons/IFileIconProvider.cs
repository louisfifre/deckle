using Microsoft.UI.Xaml.Media;

namespace Deckle.Atelier.Views;

// Maps a path to the icon a list shows for it (spec, File list: every file
// shows an icon for its type). One seam, so that the icons Windows itself
// draws can come in without touching a tool (spec, Open 31).
//
// Asynchronous and keyed by path and pixel size, because the shell's icons
// are images drawn at the size asked for, read off the UI thread and cached
// per extension and size (per file for .exe, .lnk, .ico and folders). Null
// means "no image": the row keeps its neutral glyph, so a list never waits
// for its icons and an icon that cannot be read costs nothing.
public interface IFileIconProvider
{
    // pixelSize is in physical pixels: the cell's size in DIPs times the
    // XamlRoot's rasterization scale. Called on the UI thread; the returned
    // ImageSource is used on it.
    Task<ImageSource?> GetAsync(string path, bool isFolder, int pixelSize, CancellationToken cancellation);

    // The picture of the file's content, as File Explorer's large views show
    // it (a photo, a PDF's first page), at most pixelSize on its longer side.
    // Per file, never per extension. Null means "no picture": the gallery
    // keeps its placeholder.
    Task<ImageSource?> GetThumbnailAsync(string path, int pixelSize, CancellationToken cancellation);
}

// For now: no image, so every row shows the neutral folder or file glyph.
internal sealed class NeutralFileIconProvider : IFileIconProvider
{
    public Task<ImageSource?> GetAsync(string path, bool isFolder, int pixelSize, CancellationToken cancellation) =>
        Task.FromResult<ImageSource?>(null);

    public Task<ImageSource?> GetThumbnailAsync(string path, int pixelSize, CancellationToken cancellation) =>
        Task.FromResult<ImageSource?>(null);
}

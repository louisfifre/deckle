namespace Deckle.Atelier;

// What the shell draws for a file, asked the way File Explorer asks it. Every
// method blocks on the shell and runs on a ShellWorkQueue's thread; each
// answers null when there is no picture, never throws for a file the shell
// cannot draw (gone, locked, no handler): a row without its icon keeps its
// neutral glyph, and that costs nothing.
public static class ShellImageReader
{
    // The icon of the file's type, or the file's own (a program, a shortcut),
    // drawn at pixelSize: never a thumbnail, which would read the content.
    public static ShellImage? ReadIcon(string path, int pixelSize) =>
        Read(path, pixelSize, ShellImageNativeMethods.SIIGBF_ICONONLY);

    // The picture of the file's content, from the shell's thumbnail cache
    // only: SIIGBF_INCACHEONLY "allows access to the disk, but only to
    // retrieve a cached item ... does not extract a thumbnail" (Microsoft
    // Learn). Cheap, so a picture already seen in File Explorer shows at once.
    public static ShellImage? ReadCachedThumbnail(string path, int pixelSize) =>
        Read(path, pixelSize, ThumbnailFlags | ShellImageNativeMethods.SIIGBF_INCACHEONLY);

    // The picture of the file's content, drawn by its thumbnail provider if
    // the cache has none: this reads the file, so it is asked only of a file
    // whose content is on the disk (FileContentPresence). BIGGERSIZEOK lets
    // the shell hand back the size it holds (a 256 for a 200 asked) instead of
    // shrinking it with a GDI stretch; the Image control scales it down with
    // a better filter.
    public static ShellImage? ExtractThumbnail(string path, int pixelSize) =>
        Read(path, pixelSize, ThumbnailFlags);

    // THUMBNAILONLY: "return only the thumbnail, never the icon", so a file
    // without a picture answers null and the gallery keeps its placeholder.
    private const int ThumbnailFlags =
        ShellImageNativeMethods.SIIGBF_THUMBNAILONLY | ShellImageNativeMethods.SIIGBF_BIGGERSIZEOK;

    private static ShellImage? Read(string path, int pixelSize, int flags)
    {
        if (pixelSize <= 0 || string.IsNullOrEmpty(path)) return null;
        try
        {
            return ShellImageNativeMethods.TryGetImage(path, pixelSize, flags, out ShellImage? image) >= 0 ? image : null;
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.SEHException)
        {
            return null;
        }
    }
}

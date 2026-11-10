using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Deckle.Atelier.Views;

// The icons and thumbnails Windows itself draws, the ones File Explorer shows
// (spec, Open 31: no icon set of our own to maintain).
//
// Threading. Called on the UI thread; every shell call runs on one of two
// threads of its own (ShellWorkQueue says why a thread in a single-threaded
// apartment and not the pool), and only the copy into a WriteableBitmap comes
// back to the UI thread, the one a XAML object can be created on. Two queues,
// so the cheap asks never wait behind a slow one: icons, file probes and
// cached thumbnails go on the first, thumbnail extraction, which reads a
// photo or renders a PDF page, goes on the second.
//
// Caches. Both live on the UI thread, which serialises them. Icons are keyed
// by type and size, by file for the types that draw one per file
// (FileIconKey), in a small budget: a 32-pixel icon weighs 4 KB. Thumbnails
// are keyed by file, size and last write (FileThumbnailKey) in a larger
// budget, so a gallery scrolled back and forth does not ask again, and
// memory stays bounded whatever the folder holds.
//
// Theme and scale. A file type's icon is the same in light and dark (File
// Explorer draws one set); the size asked is in physical pixels, so the
// picture is drawn at the display's scale and shown one pixel per pixel.
//
// Cancellation, and any failure, answers null: the row keeps its glyph, the
// tile its placeholder, and the caller has nothing to catch.
internal sealed class ShellFileIconProvider : IFileIconProvider, IDisposable
{
    // Interpretations, not measured: about a thousand 32-pixel icons, about
    // two hundred 256-pixel thumbnails.
    private const long IconBudgetBytes = 4L * 1024 * 1024;
    private const long ThumbnailBudgetBytes = 48L * 1024 * 1024;

    // What a "no picture" entry counts against the budget, so that remembered
    // failures are bounded too.
    private const long EmptyEntryBytes = 64;

    private readonly ShellWorkQueue _quick = new("Atelier shell icons");
    private readonly ShellWorkQueue _extraction = new("Atelier shell thumbnails");
    private readonly MemoryBoundedCache<FileIconKey, ImageSource?> _icons = new(IconBudgetBytes);
    private readonly MemoryBoundedCache<FileThumbnailKey, ImageSource?> _thumbnails = new(ThumbnailBudgetBytes);

    public async Task<ImageSource?> GetAsync(string path, bool isFolder, int pixelSize, CancellationToken cancellation)
    {
        FileIconKey key = FileIconKey.For(path, isFolder, pixelSize);
        if (_icons.TryGet(key, out ImageSource? cached)) return cached;
        try
        {
            ShellImage? image = await _quick.RunAsync(() => ShellImageReader.ReadIcon(path, pixelSize), cancellation);
            ImageSource? source = image is null ? null : ToBitmap(image);
            _icons.Add(key, source, image?.Bytes ?? EmptyEntryBytes);
            return source;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<ImageSource?> GetThumbnailAsync(string path, int pixelSize, CancellationToken cancellation)
    {
        try
        {
            FileThumbnailProbe? probe = await _quick.RunAsync(() => FileThumbnailProbe.Read(path), cancellation);
            if (probe is null) return null;

            FileThumbnailKey key = FileThumbnailKey.For(path, pixelSize, probe.LastWriteUtc);
            if (_thumbnails.TryGet(key, out ImageSource? cached)) return cached;

            ShellImage? image = await _quick.RunAsync(() => ShellImageReader.ReadCachedThumbnail(path, pixelSize), cancellation);
            // A placeholder whose content is in the cloud is never extracted:
            // reading it would download it. Its picture shows only if the
            // shell already holds one.
            if (image is null && probe.IsLocal)
                image = await _extraction.RunAsync(() => ShellImageReader.ExtractThumbnail(path, pixelSize), cancellation);

            ImageSource? source = image is null ? null : ToBitmap(image);
            _thumbnails.Add(key, source, image?.Bytes ?? EmptyEntryBytes);
            return source;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    // On the UI thread, where the await resumed. The pixels are already top
    // to bottom and premultiplied, the layout WriteableBitmap.PixelBuffer
    // holds, so they are copied as they are.
    private static WriteableBitmap ToBitmap(ShellImage image)
    {
        var bitmap = new WriteableBitmap(image.Width, image.Height);
        image.Bgra.CopyTo(bitmap.PixelBuffer);
        bitmap.Invalidate();
        return bitmap;
    }

    public void Dispose()
    {
        _quick.Dispose();
        _extraction.Dispose();
    }
}

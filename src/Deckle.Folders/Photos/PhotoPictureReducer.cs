using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Deckle.Folders;

// The picture a model is handed for a description: a JPEG of about a
// thousand pixels on its long side, made from the photo with what Windows
// ships. A vision model reads a few hundred pixels at most (it tiles or
// shrinks what it gets), so a 24-megapixel original would only cost the
// transfer and the decoding on the server.
//
// The original is only read: opened for reading, shared so the person's
// other apps keep it, never written. The JPEG is encoded from pixels alone,
// so none of the photo's metadata (date, camera, place) travels with it.
//
// Where the pixels come from, in order:
// 1. A raw file's embedded preview (BitmapDecoder.GetPreviewAsync): the
//    camera's own JPEG, much faster than developing the raw. Kept only if it
//    is at least half the size asked; some cameras embed a tiny one.
// 2. The file itself through Windows Imaging Component's codec for its type:
//    JPEG, TIFF, HEIF with its extension, and the raw formats the Raw Image
//    Extension decodes.
// 3. The shell's thumbnail of the file, the picture File Explorer shows,
//    for a format with a thumbnail provider but no codec.
// None of them answers: the photo is unreadable, and the caller says so.
//
// Off the interface thread: the decoding runs on the thread pool (WinRT
// imaging is free-threaded), the shell's thumbnail on a ShellWorkQueue's own
// thread, as the shell asks (ShellWorkQueue's header).
public static class PhotoPictureReducer
{
    public const int DefaultLongSide = 1024;

    private const float JpegQuality = 0.85f;

    public static async Task<byte[]?> ReduceAsync(
        string path, ShellWorkQueue? shell, CancellationToken cancellationToken, int longSide = DefaultLongSide)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? jpeg = await Task.Run(() => FromCodecAsync(path, longSide, cancellationToken), cancellationToken);
        if (jpeg is null && shell is not null)
            jpeg = await FromShellThumbnailAsync(path, longSide, shell, cancellationToken);
        return jpeg;
    }

    private static async Task<byte[]?> FromCodecAsync(string path, int longSide, CancellationToken ct)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using IRandomAccessStream stream = file.AsRandomAccessStream();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);

            if (PhotoFormats.IsRaw(path) && await FromPreviewAsync(decoder, longSide, ct) is { } preview)
                return preview;

            return await EncodeAsync(decoder, longSide, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // No codec for the type (WINCODEC_ERR_COMPONENTNOTFOUND), a file
            // gone or locked, a damaged file: the next source is tried.
            DeckleFoldersSource.Log.PhotoPictureSourceFailed("codec", Path.GetExtension(path).ToLowerInvariant(), ex.GetType().Name);
            return null;
        }
    }

    private static async Task<byte[]?> FromPreviewAsync(BitmapDecoder raw, int longSide, CancellationToken ct)
    {
        try
        {
            using ImageStream preview = await raw.GetPreviewAsync().AsTask(ct);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(preview).AsTask(ct);
            if (Math.Max(decoder.PixelWidth, decoder.PixelHeight) < longSide / 2) return null;
            return await EncodeAsync(decoder, longSide, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // No embedded preview: the raw codec develops the file instead.
            DeckleFoldersSource.Log.PhotoPictureSourceFailed("preview", "", ex.GetType().Name);
            return null;
        }
    }

    private static async Task<byte[]> EncodeAsync(BitmapDecoder decoder, int longSide, CancellationToken ct)
    {
        // The scale is set on the frame as stored; the Exif orientation is
        // applied after it, so a portrait photo comes out upright with its
        // proportions (checked by PhotoPictureReducerTests).
        (uint width, uint height) = Fit(decoder.PixelWidth, decoder.PixelHeight, longSide);
        var transform = new BitmapTransform
        {
            ScaledWidth = width,
            ScaledHeight = height,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(ct);
        return await EncodeJpegAsync(bitmap, ct);
    }

    private static async Task<byte[]?> FromShellThumbnailAsync(string path, int longSide, ShellWorkQueue shell, CancellationToken ct)
    {
        ShellImage? image = await shell.RunAsync(() => ShellImageReader.ExtractThumbnail(path, longSide), ct);
        if (image is null)
        {
            DeckleFoldersSource.Log.PhotoPictureSourceFailed("shell", Path.GetExtension(path).ToLowerInvariant(), "NoThumbnail");
            return null;
        }

        ShellImagePixels.OpaqueIfNoAlpha(image.Bgra);
        using SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            image.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        return await EncodeJpegAsync(bitmap, ct);
    }

    private static async Task<byte[]> EncodeJpegAsync(SoftwareBitmap bitmap, CancellationToken ct)
    {
        using var output = new InMemoryRandomAccessStream();
        var options = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(JpegQuality, PropertyType.Single),
        };
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, options).AsTask(ct);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(ct);

        using var copy = new MemoryStream();
        output.Seek(0);
        await output.AsStreamForRead().CopyToAsync(copy, ct);
        return copy.ToArray();
    }

    // The size that fits longSide on the long side, keeping the proportions;
    // a picture already smaller is never enlarged.
    internal static (uint Width, uint Height) Fit(uint width, uint height, int longSide)
    {
        uint longest = Math.Max(width, height);
        if (longest <= longSide || longest == 0) return (Math.Max(width, 1), Math.Max(height, 1));
        double scale = (double)longSide / longest;
        return ((uint)Math.Max(1, Math.Round(width * scale)), (uint)Math.Max(1, Math.Round(height * scale)));
    }
}

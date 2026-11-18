using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace Deckle.Paste.Views;

// The small preview a pasted image's row shows: drawn once from the decoded
// clipboard bitmap, scaled down to the row's square in physical pixels, so
// the window never keeps the full image after the save. A preview that
// cannot be drawn is a fallback, not a failure: the paste goes on and the
// row shows the image icon.
public sealed partial class PasteWindow
{
    // The row's square (PasteWindow.xaml), in DIPs.
    private const double ThumbnailDip = 28;

    // Scaled so the shorter side covers the square (the row fills it,
    // cropping the rest), never scaled up. Premultiplied BGRA, the format
    // SoftwareBitmapSource takes.
    private static async Task<SoftwareBitmap?> DecodeThumbnailAsync(BitmapDecoder decoder, double rasterizationScale)
    {
        try
        {
            uint width = decoder.PixelWidth, height = decoder.PixelHeight;
            double target = Math.Ceiling(ThumbnailDip * rasterizationScale);
            double factor = Math.Min(1.0, target / Math.Max(1, Math.Min(width, height)));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(width * factor)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(height * factor)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        }
        catch (Exception ex)
        {
            PasteWindowLog.ThumbnailFailed(ex);
            return null;
        }
    }

    // The decoded preview made something a row can draw, on the UI thread.
    private static async Task<ImageSource?> ToImageSourceAsync(SoftwareBitmap? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(thumbnail);
            return source;
        }
        catch (Exception ex)
        {
            PasteWindowLog.ThumbnailFailed(ex);
            return null;
        }
        finally
        {
            thumbnail.Dispose();
        }
    }
}

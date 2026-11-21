using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Deckle.Folders;

// The first page of a PDF, drawn by the renderer Windows ships
// (Windows.Data.Pdf), for the gallery's tile (spec, Behaviours: "it also
// shows PDFs and Markdown files large"). A plain Windows has no thumbnail
// provider for PDFs, so the shell's thumbnail alone would leave every PDF on
// its type icon.
//
// Threading. Windows.Data.Pdf's objects are agile and its work runs on the
// thread pool, so this needs no thread of its own, unlike the shell
// (ShellWorkQueue). The caller bounds how many run at once.
//
// The page is drawn at most pixelSize on its longer side, on white, as a page
// is printed: a PDF with no background of its own is otherwise transparent.
// The answer has ShellImage's layout, so it is cached and shown the way a
// shell thumbnail is.
//
// Never throws for a document it cannot draw: a protected PDF, a file that
// is not a PDF, gone or refused, answers null, which keeps the tile's type
// icon, and is told once (DeckleFoldersSource.PreviewNotDrawn). Cancellation
// is the one exception that leaves, for the caller to stop waiting.
public static class PdfFirstPageReader
{
    // ERROR_WRONG_PASSWORD, what LoadFromFileAsync answers for a document
    // that asks for a password it was not given. Interpretation from the
    // Win32 code's meaning: not seen on a protected document here.
    private const int WrongPasswordHResult = unchecked((int)0x8007052B);

    public static bool Previews(string path) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static async Task<ShellImage?> ReadAsync(string path, int pixelSize, CancellationToken cancellation)
    {
        if (pixelSize <= 0) return null;
        try
        {
            // Checked between the steps too: an operation that is already
            // complete when AsTask sees it ignores the token.
            cancellation.ThrowIfCancellationRequested();
            StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellation);
            cancellation.ThrowIfCancellationRequested();
            PdfDocument document = await PdfDocument.LoadFromFileAsync(file).AsTask(cancellation);
            if (document.PageCount == 0) return null;
            cancellation.ThrowIfCancellationRequested();

            using PdfPage page = document.GetPage(0);
            (uint width, uint height) = Fit(page.Size.Width, page.Size.Height, pixelSize);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = width,
                DestinationHeight = height,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
            };
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options).AsTask(cancellation);

            // The destination is in DIPs, which the renderer multiplies by
            // the system's display scale: on the machine this was written on,
            // a 160 asked came back 288. The decoder brings it to the pixels asked, whatever the
            // scale, with a filter made for shrinking.
            cancellation.ThrowIfCancellationRequested();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellation);
            var transform = new BitmapTransform
            {
                ScaledWidth = width,
                ScaledHeight = height,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            PixelDataProvider pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancellation);
            return new ShellImage((int)width, (int)height, pixels.DetachPixelData());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        // Any other: a WinRT failure reaches .NET as a COMException, an
        // IOException or a plain Exception depending on its code, and none of
        // them is the gallery's to stop for.
        catch (Exception ex)
        {
            string reason = ex.HResult == WrongPasswordHResult ? "protected" : "unreadable";
            DeckleFoldersSource.Log.PreviewNotDrawn("pdf", reason, ex.GetType().Name, ex.HResult);
            return null;
        }
    }

    // The page's size in pixels, its longer side pixelSize, its proportions
    // kept. A page with no size (a broken document) is drawn square.
    internal static (uint Width, uint Height) Fit(double pageWidth, double pageHeight, int pixelSize)
    {
        if (pageWidth <= 0 || pageHeight <= 0) return ((uint)pixelSize, (uint)pixelSize);
        double scale = pixelSize / Math.Max(pageWidth, pageHeight);
        return ((uint)Math.Max(1, Math.Round(pageWidth * scale)), (uint)Math.Max(1, Math.Round(pageHeight * scale)));
    }
}

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Deckle.App.SelfCheck;

// One page as an image, all of it. The window capture (WindowSnapshot) shows what
// the window shows, so a page taller than the window is cut at its bottom edge;
// RenderTargetBitmap renders an element at its own layout size, whatever the
// ScrollViewer around it clips. The settings pages put their whole content, the
// H1 header included, in the content of their ScrollViewer, so rendering that
// content is rendering the page.
//
// What it leaves out: the window around the page (rail, title bar, backdrop),
// which RenderTargetBitmap cannot see. The page is also drawn without the
// surface it sits on, so it is flattened onto that surface's colour: an
// approximation of the Windows 11 content layer, not the pixel Mica gives.
//
// A texture has a size limit. A page taller than MaxPixels is rendered in
// viewport-sized parts, scrolling the ScrollViewer between them.
internal static class ElementImage
{
    // Direct3D 11 feature level 11 guarantees textures up to 16384 pixels.
    internal const int MaxPixels = 16384;

    // The content layer of the NavigationView over Mica, flattened. Light is read
    // off a real capture of the Settings window (0xF9F9F9); dark is the Windows
    // 11 layer over the dark base and has not been compared with a capture.
    private static readonly Windows.UI.Color LightSurface = Windows.UI.Color.FromArgb(255, 0xF9, 0xF9, 0xF9);
    private static readonly Windows.UI.Color DarkSurface = Windows.UI.Color.FromArgb(255, 0x2B, 0x2B, 0x2B);

    // The room left around a page, in effective pixels, filled with the
    // surface's colour. The content of a page's scroller is rendered at its own
    // bounds, without the padding the page keeps around it in the window: the
    // title and the cards then touch the image's edges, and a reader cannot
    // judge the page as it sits in the app. The margin puts that room back.
    // It does not bring back what the element draws outside its bounds (a
    // shadow, a focus rectangle): RenderTargetBitmap never has those pixels.
    internal const int PageMargin = 32;

    internal sealed record Result(IReadOnlyList<string> Files, int Width, int Height);

    // Writes the page to <basePath>.png, or <basePath>-1.png, -2.png… when it
    // had to be cut. basePath has no extension.
    public static async Task<Result> CapturePageAsync(FrameworkElement page, string basePath)
    {
        ScrollViewer? scroller = FindScroller(page);
        if (scroller?.Content is not FrameworkElement content)
        {
            // A page with no scrolling content is its own whole.
            (string single, int w, int h) = await RenderAsync(page, basePath + ".png");
            return new Result([single], w, h);
        }

        double scale = page.XamlRoot?.RasterizationScale ?? 1.0;
        double fullPixels = content.ActualHeight * scale;
        if (fullPixels <= MaxPixels)
        {
            (string single, int w, int h) = await RenderAsync(
                content, basePath + ".png", (int)Math.Round(PageMargin * scale));
            return new Result([single], w, h);
        }

        // Too tall for one texture: the viewport, part by part.
        var files = new List<string>();
        double viewport = Math.Max(scroller.ViewportHeight, 1);
        double offset = 0;
        int width = 0, height = 0;
        for (int part = 1; offset < scroller.ExtentHeight; part++)
        {
            scroller.ChangeView(null, offset, null, disableAnimation: true);
            await NextFrameAsync();
            (string file, int w, int h) = await RenderAsync(scroller, $"{basePath}-{part}.png");
            files.Add(file);
            width = w;
            height += h;
            offset += viewport;
        }
        scroller.ChangeView(null, 0, null, disableAnimation: true);
        return new Result(files, width, height);
    }

    // The window's whole XAML content, for a window the page walk does not cover
    // (the pixels of the window itself come from WindowSnapshot when they can).
    public static async Task<Result> CaptureWindowContentAsync(Window window, string path)
    {
        if (window.Content is not FrameworkElement root)
            throw new InvalidOperationException("The window has no content to render.");
        (string file, int w, int h) = await RenderAsync(root, path);
        return new Result([file], w, h);
    }

    // margin, in image pixels, is added on the four sides of a page that fits
    // one image; the parts of a cut page and a window's content take none.
    private static async Task<(string Path, int Width, int Height)> RenderAsync(
        FrameworkElement element, string path, int margin = 0)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        if (width == 0 || height == 0)
            throw new InvalidOperationException("The element rendered to an empty image.");

        IBuffer pixels = await bitmap.GetPixelsAsync();
        byte[] bgra = new byte[pixels.Length];
        Windows.Storage.Streams.DataReader.FromBuffer(pixels).ReadBytes(bgra);

        Windows.UI.Color surface = element.ActualTheme == ElementTheme.Dark ? DarkSurface : LightSurface;
        FlattenOnto(bgra, surface);
        if (margin > 0)
        {
            bgra = Pad(bgra, width, height, margin, surface);
            width += 2 * margin;
            height += 2 * margin;
        }

        using var stream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            (uint)width, (uint)height, 96, 96, bgra);
        await encoder.FlushAsync();

        stream.Seek(0);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (FileStream file = File.Create(path))
            await stream.AsStreamForRead().CopyToAsync(file);
        return (path, width, height);
    }

    // The bitmap holds premultiplied alpha: over an opaque surface,
    // out = in + surface * (1 - alpha), per channel, and the result is opaque.
    internal static void FlattenOnto(byte[] bgra, Windows.UI.Color surface)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int inverse = 255 - bgra[i + 3];
            bgra[i] = (byte)Math.Min(255, bgra[i] + surface.B * inverse / 255);
            bgra[i + 1] = (byte)Math.Min(255, bgra[i + 1] + surface.G * inverse / 255);
            bgra[i + 2] = (byte)Math.Min(255, bgra[i + 2] + surface.R * inverse / 255);
            bgra[i + 3] = 255;
        }
    }

    // The same opaque image with margin pixels of the surface's colour on each
    // of its four sides.
    internal static byte[] Pad(byte[] bgra, int width, int height, int margin, Windows.UI.Color surface)
    {
        int paddedWidth = width + 2 * margin;
        byte[] padded = new byte[paddedWidth * (height + 2 * margin) * 4];
        for (int i = 0; i < padded.Length; i += 4)
        {
            padded[i] = surface.B;
            padded[i + 1] = surface.G;
            padded[i + 2] = surface.R;
            padded[i + 3] = 255;
        }
        for (int row = 0; row < height; row++)
            System.Buffer.BlockCopy(bgra, row * width * 4, padded, ((row + margin) * paddedWidth + margin) * 4, width * 4);
        return padded;
    }

    // The page's own scroller: the one with the largest viewport, so a small
    // scrolling list inside a card is never taken for the page.
    internal static ScrollViewer? FindScroller(DependencyObject root)
    {
        ScrollViewer? best = null;
        void Walk(DependencyObject node)
        {
            if (node is ScrollViewer sv && (best is null || sv.ViewportHeight > best.ViewportHeight))
                best = sv;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(root);
        return best;
    }

    private static Task NextFrameAsync()
    {
        var rendered = new TaskCompletionSource();
        void OnRendering(object? sender, object e)
        {
            CompositionTarget.Rendering -= OnRendering;
            rendered.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        // A window nobody sees may deliver no frame: do not wait for ever.
        return Task.WhenAny(rendered.Task, Task.Delay(1000));
    }
}

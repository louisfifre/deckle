using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Deckle.Vision;

// ── WindowSnapshot ────────────────────────────────────────────────────────────
//
// One image of one window, written as a PNG: the developer mode's interface
// capture takes one per Deckle window and per settings page, so that a review
// agent can judge the interface from files without driving it.
//
// Why Windows.Graphics.Capture. The image has to be the whole window as the
// person sees it, title bar included, because the window is what is judged.
// Three techniques were weighed (2026-11-07):
//
//   • RenderTargetBitmap.RenderAsync on the window's root element renders the
//     XAML tree only. Deckle's title bars are XAML (ExtendsContentIntoTitleBar),
//     but the caption buttons are drawn by the system and the Mica or Acrylic
//     backdrop by the compositor, so both would be missing: transparent pixels
//     where the material is, no minimize, maximize or close.
//   • Win32 PrintWindow with PW_RENDERFULLCONTENT asks the window to render
//     itself into a bitmap. Tried on the Windows Settings window, it returned
//     its splash screen, not its content: a composed window is not reliably
//     rendered that way, and every WinUI 3 window is a composed window.
//   • Windows.Graphics.Capture, on the window handle, hands over the frame DWM
//     composes for that window: title bar, caption buttons, backdrop, rounded
//     corners, whatever covers it on screen left out. It is the API Microsoft
//     ships for window capture, needs no screen-wide grab, and reuses the
//     Direct3D device Deckle.Vision already creates for the ambient capture.
//
// The third is the only one that gives the full window faithfully, so it is
// the technique. What it cannot do: a minimized or hidden window has no frame,
// so the caller skips those, and the image is what the window shows, so a page
// taller than the window is cut at the window's bottom edge.
//
// Not verified on a live Deckle window when written: the code compiles, the
// pixels were not seen. The first capture run is the check.
public static class WindowSnapshot
{
    // How long the first frame may take. DWM delivers one as soon as the
    // session starts; a window that sends none in this time is not drawing.
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(3);

    private static IDirect3DDevice? _device;
    private static bool _borderlessRequested;

    // Captures the window to a PNG at path and returns the image size in
    // physical pixels. Throws WindowSnapshotException naming the step that
    // failed. Safe to call from the UI thread: the waits are asynchronous.
    public static async Task<(int Width, int Height)> CaptureToPngAsync(nint hwnd, string path)
    {
        string step = "create_item";
        try
        {
            GraphicsCaptureItem item = WindowSnapshotInterop.CreateItemForWindow(hwnd);

            step = "create_device";
            _device ??= ScreenCaptureInterop.CreateDirect3DDevice();

            step = "request_borderless";
            await RequestBorderlessOnceAsync();

            step = "start_session";
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
            var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pool.FrameArrived += (_, _) => arrived.TrySetResult();
            using GraphicsCaptureSession session = pool.CreateCaptureSession(item);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                session.IsCursorCaptureEnabled = false;
            TryDropBorder(session);
            session.StartCapture();

            step = "first_frame";
            await arrived.Task.WaitAsync(FirstFrameTimeout);
            using Direct3D11CaptureFrame frame = pool.TryGetNextFrame()
                ?? throw new InvalidOperationException("The frame pool signalled a frame and held none.");

            step = "read_back";
            int width = frame.ContentSize.Width;
            int height = frame.ContentSize.Height;
            using SoftwareBitmap copy = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                frame.Surface, BitmapAlphaMode.Premultiplied);
            // PNG stores straight alpha; the rounded corners are the only
            // pixels that are not opaque, and they must not darken.
            using SoftwareBitmap straight = SoftwareBitmap.Convert(
                copy, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);

            step = "encode";
            using var stream = new InMemoryRandomAccessStream();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(straight);
            // The surface is the pool's size; a window resized since the pool
            // was made fills only its content size of it.
            encoder.BitmapTransform.Bounds = new BitmapBounds
            {
                X = 0,
                Y = 0,
                Width = (uint)Math.Min(width, straight.PixelWidth),
                Height = (uint)Math.Min(height, straight.PixelHeight),
            };
            await encoder.FlushAsync();

            step = "write_file";
            stream.Seek(0);
            await using (FileStream file = File.Create(path))
                await stream.AsStreamForRead().CopyToAsync(file);

            return ((int)encoder.BitmapTransform.Bounds.Width, (int)encoder.BitmapTransform.Bounds.Height);
        }
        catch (Exception ex) when (ex is not WindowSnapshotException)
        {
            throw new WindowSnapshotException(step, ex);
        }
    }

    // Windows draws a yellow border around a captured window unless the app is
    // allowed to drop it. An unpackaged app is granted that on Windows 11
    // without asking; the request is made once a process, and a refusal only
    // leaves the border on screen while the capture runs.
    // Both calls exist from Windows 10 21H1 (build 20348) on; before it, the
    // border is always drawn.
    private static async Task RequestBorderlessOnceAsync()
    {
        if (_borderlessRequested || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) return;
        _borderlessRequested = true;
        try { await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless); }
        catch (Exception) { /* the border stays; the capture goes on */ }
    }

    private static void TryDropBorder(GraphicsCaptureSession session)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348)) return;
        try { session.IsBorderRequired = false; }
        catch (Exception) { /* not granted: the border shows while the capture runs */ }
    }
}

// A window capture that produced no image, with the step that failed:
// create_item, create_device, request_borderless, start_session, first_frame,
// read_back, encode or write_file.
public sealed class WindowSnapshotException(string step, Exception inner)
    : Exception($"Window capture failed at {step}.", inner)
{
    public string Step { get; } = step;
}

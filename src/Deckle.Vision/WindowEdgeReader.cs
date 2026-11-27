using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using static Deckle.Vision.ScreenCaptureInterop;

namespace Deckle.Vision;

// ── WindowEdgeReader ──────────────────────────────────────────────────────────
//
// The pixels of one edge of one window, each time the window draws: the strip
// under the faded taskbar continues the edge of the maximized app beside it.
//
// Why the window's own frame, not the screen. The screen along the edge shows
// whatever lies there: during Windows' maximize animation the wallpaper or the
// window below, and a flyout sliding over the edge. Reading it meant waiting
// out animations and comparing colours (the strip's former holds). The
// capture of the window hands over what the window itself draws, whatever is
// in front of it and wherever DWM is animating it, so every frame read is the
// app's own colour. Windows.Graphics.Capture is the API Microsoft ships for
// it; WindowSnapshot already uses it for one image (its header weighs the
// other techniques).
//
// Cost: one GPU copy of a one-pixel line per frame the window presents, and a
// map of that line. Every frame is read: Windows sends one only when the
// window draws, and a frame dropped to save a read can be the last one, the
// edge then left at a colour it no longer shows (seen in the trial: the black
// of a window just maximized, before it painted its new area). The caller
// spaces out what it does with them.
//
// The callback runs on the capture's own thread, never the caller's.
public sealed class WindowEdgeReader : IDisposable
{
    public enum Side { Bottom, Top, Left, Right }

    // One device and one immediate context for every reader: the context is
    // not thread-safe, and each reader's frames arrive on its own thread.
    private static readonly object DeviceGate = new();
    private static IDirect3DDevice? _device;
    private static nint _nativeDevice;
    private static nint _context;

    private readonly Side _side;
    private readonly Action<uint[]> _onEdge;
    private readonly Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private SizeInt32 _poolSize;
    private nint _staging;
    private uint _stagingLength;
    private bool _disposed;

    // Windows let the capture run without its yellow border round the app.
    public bool Borderless { get; }

    private WindowEdgeReader(GraphicsCaptureItem item, Side side, Action<uint[]> onEdge)
    {
        _side = side;
        _onEdge = onEdge;
        _poolSize = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(item);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) _session.IsCursorCaptureEnabled = false;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            // Without it Windows draws a yellow border round the app while the
            // capture runs. WindowSnapshot asks for the right once a process.
            try { _session.IsBorderRequired = false; Borderless = true; } catch (Exception) { }
        }
        _session.StartCapture();
    }

    // Starts reading hwnd's side. Throws when Windows refuses the capture
    // (a window that is gone, no Direct3D device).
    public static WindowEdgeReader Start(nint hwnd, Side side, Action<uint[]> onEdge)
    {
        lock (DeviceGate)
        {
            if (_device is null)
            {
                _device = CreateDirect3DDevice();
                _nativeDevice = GetD3D11Device(_device);
                unsafe
                {
                    var vtbl = *(nint**)_nativeDevice;
                    nint context;
                    ((delegate* unmanaged<nint, nint*, void>)vtbl[D3D11Vtbl.Device_GetImmediateContext])(_nativeDevice, &context);
                    _context = context;
                }
            }
        }
        return new WindowEdgeReader(WindowSnapshotInterop.CreateItemForWindow(hwnd), side, onEdge);
    }

    // Asks once a process for the right to capture without the yellow
    // border. An unpackaged app is granted it on Windows 11 without a prompt.
    public static Task RequestBorderlessAsync() => WindowSnapshot.RequestBorderlessOnceAsync();

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
        if (frame is null) return;
        uint[]? line = null;
        lock (DeviceGate)
        {
            if (_disposed) return;
            SizeInt32 size = frame.ContentSize;
            if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
            {
                // The window changed size: the next frames come at the new one.
                _poolSize = size;
                _pool.Recreate(_device!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            }
            if (size.Width <= 0 || size.Height <= 0) return;
            line = ReadLine(frame.Surface, (uint)size.Width, (uint)size.Height);
        }
        if (line is not null) _onEdge(line);
    }

    // Copies the edge line of the frame into a staging texture and maps it.
    // Under DeviceGate.
    private unsafe uint[]? ReadLine(IDirect3DSurface surface, uint width, uint height)
    {
        bool horizontal = _side is Side.Bottom or Side.Top;
        uint length = horizontal ? width : height;
        var box = _side switch
        {
            Side.Bottom => new D3D11_BOX { Left = 0, Right = width, Top = height - 1, Bottom = height, Front = 0, Back = 1 },
            Side.Top    => new D3D11_BOX { Left = 0, Right = width, Top = 0, Bottom = 1, Front = 0, Back = 1 },
            Side.Left   => new D3D11_BOX { Left = 0, Right = 1, Top = 0, Bottom = height, Front = 0, Back = 1 },
            _           => new D3D11_BOX { Left = width - 1, Right = width, Top = 0, Bottom = height, Front = 0, Back = 1 },
        };

        if (_staging == 0 || _stagingLength != length)
        {
            if (_staging != 0) Marshal.Release(_staging);
            _staging = 0;
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = horizontal ? length : 1,
                Height = horizontal ? 1 : length,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDescCount = 1,
                Usage = D3D11_USAGE_STAGING,
                CPUAccessFlags = D3D11_CPU_ACCESS_READ,
            };
            var device = *(nint**)_nativeDevice;
            nint texture;
            int hr = ((delegate* unmanaged<nint, D3D11_TEXTURE2D_DESC*, nint, nint*, int>)device[D3D11Vtbl.Device_CreateTexture2D])(_nativeDevice, &desc, 0, &texture);
            if (hr < 0) return null;
            _staging = texture;
            _stagingLength = length;
        }

        nint source = GetD3D11Texture(surface);
        try
        {
            var ctx = *(nint**)_context;
            ((delegate* unmanaged<nint, nint, uint, uint, uint, uint, nint, uint, D3D11_BOX*, void>)ctx[D3D11Vtbl.Context_CopySubresourceRegion])(
                _context, _staging, 0, 0, 0, 0, source, 0, &box);
            D3D11_MAPPED_SUBRESOURCE mapped;
            int hr = ((delegate* unmanaged<nint, nint, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)ctx[D3D11Vtbl.Context_Map])(
                _context, _staging, 0, D3D11_MAP_READ, 0, &mapped);
            if (hr < 0) return null;
            try
            {
                var pixels = new uint[length];
                byte* row = (byte*)mapped.pData;
                // A horizontal line is one row; a vertical one is a pixel per row.
                for (uint i = 0; i < length; i++)
                    pixels[i] = (horizontal ? ((uint*)row)[i] : *(uint*)(row + i * mapped.RowPitch)) & 0x00FFFFFF;
                return pixels;
            }
            finally
            {
                ((delegate* unmanaged<nint, nint, uint, void>)ctx[D3D11Vtbl.Context_Unmap])(_context, _staging, 0);
            }
        }
        finally
        {
            Marshal.Release(source);
        }
    }

    public void Dispose()
    {
        lock (DeviceGate)
        {
            if (_disposed) return;
            _disposed = true;
            _pool.FrameArrived -= OnFrameArrived;
            _session.Dispose();
            _pool.Dispose();
            if (_staging != 0) Marshal.Release(_staging);
            _staging = 0;
        }
    }
}

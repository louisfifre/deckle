using System.Runtime.InteropServices;
using Deckle.Core;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

// One taskbar's underlay: a strip of Deckle's own, the taskbar's size, placed
// under the taskbar while an app is maximized on its monitor and painted the
// colour of that app's edge. The taskbar's alpha fades its background too, so
// without it a faded taskbar shows the wallpaper between the screen's edge
// and the app. It sits just below the maximized app in the z-order: under
// the topmost taskbar, above the desktop, and never in a fight for the top.
// Worker thread only, like the band that owns it.
internal sealed class TaskbarUnderlay : IDisposable
{
    public IntPtr Hwnd { get; }
    // The maximized app the underlay continues; zero while hidden.
    public IntPtr App { get; private set; }
    public bool Visible => App != IntPtr.Zero;
    // A maximized app found but not shown yet, and since when (TickCount64):
    // the host waits out Windows' maximize animation before reading it.
    public IntPtr PendingApp { get; set; }
    public long PendingSince { get; set; }
    // What the edge showed when the app was found, before it reached it.
    public uint? PendingBefore { get; set; }
    // The COLORREF last painted.
    public uint Color => _color;

    private IntPtr _brush;
    private uint _color = uint.MaxValue;
    // The strip shows a tinted blur instead of its plain colour.
    private bool _blur;
    private NativeMethods.RECT _rect;

    private TaskbarUnderlay(IntPtr hwnd) => Hwnd = hwnd;

    // NOACTIVATE + TOOLWINDOW like the anchor: never focused, never in
    // Alt-Tab. Returns null and the Win32 error when the window is refused.
    public static TaskbarUnderlay? Create(string className, IntPtr hInstance, out int error)
    {
        IntPtr hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
            lpClassName:  className,
            lpWindowName: null,
            dwStyle:      WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:   IntPtr.Zero,
            hMenu:        IntPtr.Zero,
            hInstance:    hInstance,
            lpParam:      IntPtr.Zero);
        error = hwnd == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        if (hwnd == IntPtr.Zero) return null;

        uint corner = DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint));
        return new TaskbarUnderlay(hwnd);
    }

    // How much of the edge colour tints the blur: enough to carry the app's
    // colour, little enough to let the blurred wallpaper through, as the
    // app's own acrylic does.
    private const uint BlurTintAlpha = 0x99;

    // Shows the strip over rect, just below app, in color (a COLORREF),
    // plain or as a blur tinted with it. Each part is written only when it
    // changed: this runs on every refresh.
    public void Show(IntPtr app, NativeMethods.RECT rect, uint color, bool blur)
    {
        if (color != _color || blur != _blur)
        {
            if (color != _color)
            {
                if (_brush != IntPtr.Zero) DeleteObject(_brush);
                _brush = CreateSolidBrush(color);
            }
            // A refused blur leaves the plain colour.
            bool wasBlur = _blur;
            _blur = blur && SetAccent(ACCENT_ENABLE_ACRYLICBLURBEHIND, BlurTintAlpha << 24 | color);
            if (wasBlur && !_blur) SetAccent(ACCENT_DISABLED, 0);
            _color = color;
            InvalidateRect(Hwnd, IntPtr.Zero, true);
        }

        bool moved = rect.left != _rect.left || rect.top != _rect.top
                  || rect.right != _rect.right || rect.bottom != _rect.bottom;
        if (app == App && !moved) return;

        NativeMethods.SetWindowPos(Hwnd, app,
            rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        App = app;
        _rect = rect;
    }

    private bool SetAccent(int state, uint tint)
    {
        var policy = new ACCENT_POLICY
        {
            AccentState   = state,
            AccentFlags   = ACCENT_FLAG_GRADIENT_COLOR,
            GradientColor = tint,
        };
        IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENT_POLICY>());
        try
        {
            Marshal.StructureToPtr(policy, p, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attrib = WCA_ACCENT_POLICY,
                pvData = p,
                cbData = Marshal.SizeOf<ACCENT_POLICY>(),
            };
            return SetWindowCompositionAttribute(Hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    public void Hide()
    {
        if (!Visible) return;
        NativeMethods.ShowWindow(Hwnd, NativeMethods.SW_HIDE);
        App = IntPtr.Zero;
    }

    // WM_ERASEBKGND: the whole strip in one colour, or black under the blur.
    public void Paint(IntPtr hdc)
    {
        IntPtr brush = _blur ? GetStockObject(BLACK_BRUSH) : _brush;
        if (brush == IntPtr.Zero || !GetClientRect(Hwnd, out var client)) return;
        FillRect(hdc, ref client, brush);
    }

    public void Dispose()
    {
        NativeMethods.DestroyWindow(Hwnd);
        if (_brush != IntPtr.Zero) DeleteObject(_brush);
        _brush = IntPtr.Zero;
    }

    // ── Reading the screen ────────────────────────────────────────────────

    // The topmost maximized app on monitor, on the current virtual desktop;
    // zero when there is none. Always-on-top windows are left out: the
    // underlay placed under one would become always-on-top itself.
    public static IntPtr FindMaximizedApp(IntPtr monitor)
    {
        for (IntPtr w = GetTopWindow(IntPtr.Zero); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDNEXT))
        {
            if (!IsZoomed(w) || !IsWindowVisible(w)) continue;
            if ((NativeMethods.GetWindowLongPtr(w, GWL_EXSTYLE).ToInt64() & NativeMethods.WS_EX_TOPMOST) != 0) continue;
            if (DwmGetWindowAttribute(w, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            if (MonitorFromWindow(w, MONITOR_DEFAULTTONEAREST) != monitor) continue;
            return w;
        }
        return IntPtr.Zero;
    }

    // The app shows a see-through backdrop right now: it asked Windows for
    // Mica or Acrylic, and it holds the focus — Windows draws an inactive
    // window's backdrop as a plain colour, and the strip follows the app.
    public static bool ShowsBackdrop(IntPtr app)
    {
        if (DwmGetWindowAttribute(app, DWMWA_SYSTEMBACKDROP_TYPE, out int type, sizeof(int)) != 0) return false;
        if (type is < 2 or > 4) return false;
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        return foreground != IntPtr.Zero && GetAncestor(foreground, GA_ROOTOWNER) == app;
    }

    // The dominant colour of the screen line, or null when GDI refused.
    public static uint? ReadColor(NativeMethods.RECT line)
    {
        int width = line.right - line.left, height = line.bottom - line.top;
        if (width <= 0 || height <= 0) return null;

        var header = new BITMAPINFOHEADER
        {
            biSize     = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth    = width,
            biHeight   = -height, // top-down
            biPlanes   = 1,
            biBitCount = 32,
        };

        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr bitmap = CreateDIBSection(screen, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
        try
        {
            if (screen == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero) return null;
            IntPtr previous = SelectObject(memory, bitmap);
            bool copied = BitBlt(memory, 0, 0, width, height, screen, line.left, line.top, SRCCOPY);
            SelectObject(memory, previous);
            if (!copied) return null;
            GdiFlush();

            var pixels = new int[width * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return EdgeColor.Dominant(MemoryMarshal.Cast<int, uint>(pixels));
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

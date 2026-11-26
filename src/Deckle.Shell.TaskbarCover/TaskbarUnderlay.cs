using System.Runtime.InteropServices;
using Deckle.Core;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

// One taskbar's underlay: a strip of Deckle's own, the taskbar's size, placed
// under the taskbar while an app is maximized on its monitor and painted the
// colour of that app's edge. The taskbar's alpha fades its background too, so
// without it a faded taskbar shows the wallpaper between the screen's edge
// and the app. It sits below the topmost taskbar, above the app it continues.
//
// The window is created owned by the app, one per app shown, and destroyed
// when hidden. An owned window lives on its owner's virtual desktop, while a
// tool window without an owner shows on every desktop (seen in Task view: a
// strip beside the wallpaper of desktops with no maximized app). Windows
// reads the owner at creation only, so a new app gets a new window. Windows
// destroys an owned window with its owner; IsWindow catches it.
// Worker thread only, like the band that owns it.
internal sealed class TaskbarUnderlay : IDisposable
{
    private readonly string _className;
    private readonly IntPtr _hInstance;

    public IntPtr Hwnd { get; private set; }
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
    // The last read edge showed more than one colour (EdgeColor.PlainShare).
    public bool EdgeVaried { get; set; }

    private IntPtr _brush;
    private uint _color = uint.MaxValue;
    // The strip shows a tinted blur instead of its plain colour.
    private bool _blur;
    private NativeMethods.RECT _rect;

    public TaskbarUnderlay(string className, IntPtr hInstance)
    {
        _className = className;
        _hInstance = hInstance;
    }

    // How much of the edge colour tints the blur: enough to carry the app's
    // colour, little enough to let the blurred wallpaper through, as the
    // app's own acrylic does.
    private const uint BlurTintAlpha = 0x99;

    // Shows the strip over rect beside app, in color (a COLORREF), plain or
    // as a blur tinted with it. Each part is written only when it changed:
    // this runs on every refresh. Returns 0, or the Win32 error of a window
    // Windows refused, the strip then staying hidden.
    public int Show(IntPtr app, NativeMethods.RECT rect, uint color, bool blur)
    {
        bool created = false;
        if (app != App || !IsWindow(Hwnd))
        {
            DestroyStrip();
            // NOACTIVATE + TOOLWINDOW like the anchor: never focused, never
            // in Alt-Tab.
            Hwnd = NativeMethods.CreateWindowEx(
                dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
                lpClassName:  _className,
                lpWindowName: null,
                dwStyle:      WS_POPUP,
                x: 0, y: 0, nWidth: 0, nHeight: 0,
                hWndParent:   app,
                hMenu:        IntPtr.Zero,
                hInstance:    _hInstance,
                lpParam:      IntPtr.Zero);
            if (Hwnd == IntPtr.Zero) return Marshal.GetLastWin32Error();

            uint corner = DWMWCP_DONOTROUND;
            NativeMethods.DwmSetWindowAttribute(Hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint));
            _color = uint.MaxValue;
            _blur = false;
            created = true;
        }

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
        if (!created && !moved) return 0;

        // An owned window stays above its owner on its own: no z-order to set.
        NativeMethods.SetWindowPos(Hwnd, IntPtr.Zero,
            rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        App = app;
        _rect = rect;
        return 0;
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
        DestroyStrip();
    }

    private void DestroyStrip()
    {
        if (Hwnd != IntPtr.Zero && IsWindow(Hwnd)) NativeMethods.DestroyWindow(Hwnd);
        Hwnd = IntPtr.Zero;
        App = IntPtr.Zero;
        _rect = default;
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
        DestroyStrip();
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

    // The app itself is what the screen shows along the line: no flyout
    // (notification centre, Start, search) or other window lies over its edge.
    // Checked at both ends and the middle.
    public static bool AppOwnsLine(IntPtr app, NativeMethods.RECT line)
    {
        int length = line.bottom - line.top;
        bool vertical = line.right - line.left == 1;
        if (!vertical) length = line.right - line.left;
        foreach (int at in (ReadOnlySpan<int>)[length / 10, length / 2, length * 9 / 10])
        {
            var p = vertical
                ? new POINT { X = line.left, Y = line.top + at }
                : new POINT { X = line.left + at, Y = line.top };
            if (GetAncestor(WindowFromPoint(p), GA_ROOT) != app) return false;
        }
        return true;
    }

    // Task view is open: its desktop previews would show the strip as a bar
    // beside the app, with no taskbar over it.
    public static bool TaskViewShown()
    {
        var name = new System.Text.StringBuilder(64);
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        return foreground != IntPtr.Zero && GetClassName(foreground, name, name.Capacity) > 0
            && name.ToString() == TaskViewClass;
    }

    // The dominant colour of the screen line and the share of the line it
    // covers, or null when GDI refused.
    public static (uint Color, double Share)? ReadColor(NativeMethods.RECT line)
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
            uint color = EdgeColor.Dominant(MemoryMarshal.Cast<int, uint>(pixels), out double share);
            return (color, share);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

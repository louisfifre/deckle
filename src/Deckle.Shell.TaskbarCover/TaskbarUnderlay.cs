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
//
// Motion, while animations are on: every change dissolves. The strip fades
// in when it shows and out when it hides. A new look (colour, blur on or
// off, or another app's edge) comes in under the old one, which fades out
// over it. The whole window fades, its blur too (WS_EX_LAYERED alpha, seen in
// the trial to fade the acrylic with it), so a blur rises and falls with its
// opacity. The new look goes under the old one because an acrylic window
// blurs what lies under it: a blur fading in over the plain strip would blur
// the strip, not the app's backdrop. A slide out of the app's edge was tried
// first and dropped: it set off once the window had arrived and read as a
// stiff move of its own (Louis, trial of 2026-11-26). Without animations
// every change is instant. The curve and duration are UnderlayMotion's.
// Worker thread only, like the band that owns it.
internal sealed class TaskbarUnderlay : IDisposable
{
    private readonly string _className;
    private readonly IntPtr _hInstance;

    // The maximized app the underlay continues; zero while hidden, fading
    // out included.
    public IntPtr App { get; private set; }
    public bool Visible => App != IntPtr.Zero;
    // A maximized app found but not shown yet, and since when (TickCount64):
    // the host waits out Windows' maximize animation before reading it.
    public IntPtr PendingApp { get; set; }
    public long PendingSince { get; set; }
    // What the edge showed when the app was found, before it reached it.
    public uint? PendingBefore { get; set; }
    // The COLORREF last asked for.
    public uint Color => _current?.Color ?? uint.MaxValue;
    // The last read edge showed more than one colour (EdgeColor.PlainShare).
    public bool EdgeVaried { get; set; }
    // A fade is under way: the host ticks it.
    public bool Animating => _current?.Fading == true || _leaving is not null;

    // The strip showing the look asked for (null while hidden), and the one
    // fading out over it.
    private Strip? _current;
    private Strip? _leaving;
    private NativeMethods.RECT _rect;

    public TaskbarUnderlay(string className, IntPtr hInstance)
    {
        _className = className;
        _hInstance = hInstance;
    }

    // Shows the strip over rect beside app, in color (a COLORREF), plain or
    // as a blur tinted with it, dissolving there when animate. Each part is
    // written only when it changed: this runs on every refresh. Returns 0, or
    // the Win32 error of a window Windows refused, the strip then staying
    // hidden.
    public int Show(IntPtr app, NativeMethods.RECT rect, uint color, bool blur, bool animate)
    {
        bool moved = rect.left != _rect.left || rect.top != _rect.top
                  || rect.right != _rect.right || rect.bottom != _rect.bottom;
        _rect = rect;
        App = app;

        if (_current is not null && !IsWindow(_current.Hwnd)) _current = null;
        if (_leaving is not null && !IsWindow(_leaving.Hwnd)) _leaving = null;

        if (!animate)
        {
            _leaving?.Destroy();
            _leaving = null;
            // Another app's strip is shown before the old one goes: destroyed
            // first, the screen showed no strip for a frame or two (measured,
            // 28 ms).
            if (_current is null || _current.Owner != app)
            {
                var next = Strip.Create(_className, _hInstance, app, color, blur, 255, out int error);
                if (next is null) return error;
                next.Place(_rect, _current?.Hwnd ?? IntPtr.Zero);
                _current?.Destroy();
                _current = next;
            }
            _current.SetLook(color, blur);
            _current.SetAlpha(255);
            if (moved) Place();
            return 0;
        }

        // The same app back while its strip fades out: it fades back in.
        if (_current is null && _leaving is { } back && back.Owner == app && back.Color == color && back.Blur == blur)
        {
            _current = back;
            _leaving = null;
            _current.FadeTo(255);
            Place();
            return 0;
        }

        if (_current is null || _current.Owner != app || _current.Color != color || _current.Blur != blur)
        {
            // The new look starts where the strip it replaces stood, from
            // nothing when none shows, and rises under the old one fading out.
            byte from = _current?.Alpha ?? 0;
            var next = Strip.Create(_className, _hInstance, app, color, blur, from, out int error);
            if (next is null) return error;
            next.FadeTo(255);
            if (_current is not null)
            {
                _leaving?.Destroy();
                _leaving = _current;
                _leaving.FadeTo(0);
            }
            _current = next;
            moved = true;
        }

        if (moved) Place();
        return 0;
    }

    // Hides the strip, fading it out when animate.
    public void Hide(bool animate)
    {
        if (!Visible) return;
        App = IntPtr.Zero;
        if (!animate || _current is null)
        {
            DestroyStrips();
            return;
        }
        _leaving?.Destroy();
        _leaving = _current;
        _leaving.FadeTo(0);
        _current = null;
    }

    // One frame of the fades, at now (TickCount64).
    public void Tick(long now)
    {
        _current?.Tick(now);
        if (_leaving is not null && _leaving.Tick(now) && _leaving.Alpha == 0)
        {
            _leaving.Destroy();
            _leaving = null;
        }
    }

    // Puts the strips under the taskbar, the fading one over the new one,
    // both above the app.
    private void Place()
    {
        _leaving?.Place(_rect, IntPtr.Zero);
        _current?.Place(_rect, _leaving?.Hwnd ?? IntPtr.Zero);
    }

    private void DestroyStrips()
    {
        _leaving?.Destroy();
        _leaving = null;
        _current?.Destroy();
        _current = null;
        App = IntPtr.Zero;
    }

    // WM_ERASEBKGND for one of the strips; false when hwnd is neither.
    public bool TryPaint(IntPtr hwnd, IntPtr hdc)
    {
        var strip = _current?.Hwnd == hwnd ? _current : _leaving?.Hwnd == hwnd ? _leaving : null;
        if (strip is null) return false;
        strip.Paint(hdc);
        return true;
    }

    public void Dispose() => DestroyStrips();

    // One window of the underlay, with its look (a plain colour or a blur
    // tinted with it) and its fade.
    private sealed class Strip
    {
        // How much of the edge colour tints the blur: enough to carry the
        // app's colour, little enough to let the blurred wallpaper through,
        // as the app's own acrylic does.
        private const uint BlurTintAlpha = 0x99;

        public IntPtr Hwnd { get; private init; }
        public IntPtr Owner { get; private init; }
        public uint Color { get; private set; } = uint.MaxValue;
        public bool Blur { get; private set; }
        public byte Alpha { get; private set; }
        public bool Fading => _fadeStart != 0;
        private IntPtr _brush;
        private byte _fadeFrom, _fadeTo;
        private long _fadeStart;

        public static Strip? Create(string className, IntPtr hInstance, IntPtr owner, uint color, bool blur, byte alpha, out int error)
        {
            // NOACTIVATE + TOOLWINDOW like the anchor: never focused, never
            // in Alt-Tab. LAYERED for the fade.
            IntPtr hwnd = NativeMethods.CreateWindowEx(
                dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_LAYERED,
                lpClassName:  className,
                lpWindowName: null,
                dwStyle:      WS_POPUP,
                x: 0, y: 0, nWidth: 0, nHeight: 0,
                hWndParent:   owner,
                hMenu:        IntPtr.Zero,
                hInstance:    hInstance,
                lpParam:      IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                return null;
            }
            error = 0;

            uint corner = DWMWCP_DONOTROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint));
            var strip = new Strip { Hwnd = hwnd, Owner = owner };
            // A layered window shows nothing until its alpha is set.
            strip.SetAlpha(alpha);
            strip.SetLook(color, blur);
            return strip;
        }

        // Starts a fade from the alpha shown now.
        public void FadeTo(byte to)
        {
            _fadeFrom = Alpha;
            _fadeTo = to;
            _fadeStart = Alpha == to ? 0 : Environment.TickCount64;
        }

        // One frame of the fade; true when it is over.
        public bool Tick(long now)
        {
            if (_fadeStart == 0) return true;
            long elapsed = now - _fadeStart;
            SetAlpha(UnderlayMotion.FadeAlpha(_fadeFrom, _fadeTo, elapsed));
            if (!UnderlayMotion.FadeDone(elapsed)) return false;
            _fadeStart = 0;
            return true;
        }

        public void SetLook(uint color, bool blur)
        {
            if (color == Color && blur == Blur) return;
            if (color != Color)
            {
                if (_brush != IntPtr.Zero) DeleteObject(_brush);
                _brush = CreateSolidBrush(color);
            }
            // A refused blur leaves the plain colour.
            bool wasBlur = Blur;
            Blur = blur && SetAccent(ACCENT_ENABLE_ACRYLICBLURBEHIND, BlurTintAlpha << 24 | color);
            if (wasBlur && !Blur) SetAccent(ACCENT_DISABLED, 0);
            Color = color;
            InvalidateRect(Hwnd, IntPtr.Zero, true);
        }

        public void SetAlpha(byte alpha)
        {
            Alpha = alpha;
            NativeMethods.SetLayeredWindowAttributes(Hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
        }

        // Over r, just under the window above, or where it is when zero:
        // an owned window stays above its owner on its own.
        public void Place(NativeMethods.RECT r, IntPtr above)
        {
            uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
            if (above == IntPtr.Zero) flags |= NativeMethods.SWP_NOZORDER;
            NativeMethods.SetWindowPos(Hwnd, above, r.left, r.top, r.right - r.left, r.bottom - r.top, flags);
        }

        // WM_ERASEBKGND: the whole strip in one colour, or black under the
        // blur, which the accent draws as see-through.
        public void Paint(IntPtr hdc)
        {
            IntPtr brush = Blur ? GetStockObject(BLACK_BRUSH) : _brush;
            if (brush == IntPtr.Zero || !GetClientRect(Hwnd, out var client)) return;
            FillRect(hdc, ref client, brush);
        }

        public void Destroy()
        {
            if (IsWindow(Hwnd)) NativeMethods.DestroyWindow(Hwnd);
            if (_brush != IntPtr.Zero) DeleteObject(_brush);
            _brush = IntPtr.Zero;
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

    // Which of the shell's full-screen views holds the foreground: Task
    // view, whose desktop previews would show the strip as a bar beside the
    // app with no taskbar over it, or Alt+Tab's switcher, which leaves the
    // desktop as it is under its list.
    public enum ShellView { None, TaskView, TaskSwitcher }

    public static ShellView ForegroundShellView()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        var text = new System.Text.StringBuilder(64);
        if (foreground == IntPtr.Zero || GetClassName(foreground, text, text.Capacity) == 0
            || text.ToString() != TaskViewClass)
            return ShellView.None;
        text.Clear();
        GetWindowText(foreground, text, text.Capacity);
        return text.ToString() == TaskSwitcherTitle ? ShellView.TaskSwitcher : ShellView.TaskView;
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

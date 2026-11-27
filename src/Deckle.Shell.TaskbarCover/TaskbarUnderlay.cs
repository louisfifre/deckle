using System.Runtime.InteropServices;
using Deckle.Core;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

// One taskbar's underlay: a strip of Deckle's own, the taskbar's size, placed
// under the taskbar while an app is maximized on its monitor and painted the
// colour of that app's edge. The taskbar's alpha fades its background too, so
// without it a faded taskbar shows the wallpaper between the screen's edge
// and the app. It sits below the topmost taskbar, just below the app it
// continues: the strip lies beside the app, never over it, so no app shows
// through it, and a window over the app stays over the strip.
//
// One window, made once and never hidden nor destroyed while the band
// lives: it only ever changes its opacity and its colour (UnderlayFade).
// Windows animates a window that opens, closes or follows its owner into
// the taskbar, and DWMWA_TRANSITIONS_FORCEDISABLED did not stop it on a
// strip made and destroyed per change and owned by its app (Louis, trial of
// v0.159.0: the strip opened, closed and minimized like a window under its
// own fade). A layered window's alpha is no show nor hide, and a window with
// no owner is not taken along when an app is minimized. With no owner the
// strip shows on every virtual desktop, as the taskbar does: a switch of
// desktop leaves it in place under the taskbar and fades it to the next
// desktop's app, or away.
//
// Motion, while animations are on: every change dissolves, the same fade
// for each. The strip fades in when it shows and out when it hides; a new
// colour dissolves into the old one; a blur rises as the tint over it thins,
// and falls as it thickens back to the plain colour. Without animations
// every change is instant. The curve and duration are UnderlayMotion's.
// Worker thread only, like the band that owns it.
internal sealed class TaskbarUnderlay : IDisposable
{
    // How much of the edge colour tints the blur: enough to carry the app's
    // colour, little enough to let the blurred wallpaper through, as the
    // app's own acrylic does. 255, the plain strip, is the tint at its full.
    private const byte BlurTint = 0x99;
    private const byte PlainTint = 0xFF;

    private readonly string _className;
    private readonly IntPtr _hInstance;
    private readonly UnderlayFade _fade = new();
    private IntPtr _hwnd;
    private NativeMethods.RECT _rect;
    // What the window was last given, so a frame writes only what changed.
    private UnderlayLook _painted;
    private bool _accentOn;
    // Windows refused the blur once: the strip stays plain.
    private bool _blurRefused;
    private IntPtr _brush;

    // The maximized app the underlay continues; zero while hidden, fading
    // out included.
    public IntPtr App { get; private set; }
    public bool Visible => App != IntPtr.Zero;
    // The COLORREF last asked for.
    public uint Color => _fade.Target.Color;
    // The last read edge showed more than one colour (EdgeColor.PlainShare).
    public bool EdgeVaried { get; set; }
    // A fade is under way: the host ticks it.
    public bool Animating => _fade.Moving;

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
        if (!EnsureWindow(out int error)) return error;
        App = app;
        Place(rect, app);
        var target = new UnderlayLook(255, color, blur && !_blurRefused ? BlurTint : PlainTint);
        _fade.MoveTo(target, animate, Environment.TickCount64);
        Paint();
        return 0;
    }

    // Hides the strip, fading it out when animate. It stays where it is,
    // with its colour, at no opacity.
    public void Hide(bool animate)
    {
        if (!Visible) return;
        App = IntPtr.Zero;
        _fade.MoveTo(_fade.Target with { Alpha = 0 }, animate, Environment.TickCount64);
        Paint();
    }

    // One frame of the fade, at now (TickCount64).
    public void Tick(long now)
    {
        _fade.Tick(now);
        Paint();
    }

    // Made the first time the strip shows, at no opacity, then shown once
    // for good. IsWindow catches a window destroyed from outside.
    private bool EnsureWindow(out int error)
    {
        error = 0;
        if (_hwnd != IntPtr.Zero && IsWindow(_hwnd)) return true;
        // NOACTIVATE + TOOLWINDOW like the anchor: never focused, never in
        // Alt-Tab. LAYERED for the fade, TRANSPARENT so that a strip at no
        // opacity takes no click.
        IntPtr hwnd = NativeMethods.CreateWindowEx(
            dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE
                        | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT,
            lpClassName:  _className,
            lpWindowName: null,
            dwStyle:      WS_POPUP,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent:   IntPtr.Zero,
            hMenu:        IntPtr.Zero,
            hInstance:    _hInstance,
            lpParam:      IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        uint corner = DWMWCP_DONOTROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint));
        // Shown once, at no opacity: nothing of Windows' opening is seen.
        uint noTransitions = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref noTransitions, sizeof(uint));
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA);
        _hwnd = hwnd;
        _painted = default;
        _accentOn = false;
        _rect = default;
        _fade.MoveTo(default, animate: false, 0);
        return true;
    }

    // Over rect, just below app. Written only when the rect changed or
    // another window came between the app and the strip (the app raised
    // over windows the strip was still above). The first call shows the
    // window, for good.
    private void Place(NativeMethods.RECT rect, IntPtr app)
    {
        bool moved = rect.left != _rect.left || rect.top != _rect.top
                  || rect.right != _rect.right || rect.bottom != _rect.bottom;
        bool reorder = GetWindow(app, GW_HWNDNEXT) != _hwnd;
        if (!moved && !reorder) return;
        _rect = rect;
        uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
        if (!reorder) flags |= NativeMethods.SWP_NOZORDER;
        NativeMethods.SetWindowPos(_hwnd, reorder ? app : IntPtr.Zero,
            rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, flags);
    }

    // Writes what the fade shows now: the opacity, the colour, the blur.
    private void Paint()
    {
        if (_hwnd == IntPtr.Zero) return;
        var look = _fade.Shown;
        if (look == _painted) return;

        if (look.Color != _painted.Color || _brush == IntPtr.Zero)
        {
            if (_brush != IntPtr.Zero) DeleteObject(_brush);
            _brush = CreateSolidBrush(look.Color);
        }
        bool accent = look.Tint < PlainTint;
        if (accent && (!_accentOn || look.Tint != _painted.Tint || look.Color != _painted.Color))
        {
            // A refused blur leaves the plain colour, now and after.
            _accentOn = SetAccent(ACCENT_ENABLE_ACRYLICBLURBEHIND, (uint)look.Tint << 24 | look.Color);
            if (!_accentOn) _blurRefused = true;
        }
        else if (!accent && _accentOn)
        {
            SetAccent(ACCENT_DISABLED, 0);
            _accentOn = false;
        }
        if (look.Color != _painted.Color || accent != (_painted.Tint < PlainTint))
            InvalidateRect(_hwnd, IntPtr.Zero, true);
        if (look.Alpha != _painted.Alpha)
            NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, look.Alpha, NativeMethods.LWA_ALPHA);
        _painted = look;
    }

    // WM_ERASEBKGND for the strip; false when hwnd is not it. The whole
    // strip in one colour, or black under the blur, which the accent draws
    // as see-through.
    public bool TryPaint(IntPtr hwnd, IntPtr hdc)
    {
        if (hwnd != _hwnd || _hwnd == IntPtr.Zero) return false;
        IntPtr brush = _accentOn ? GetStockObject(BLACK_BRUSH) : _brush;
        if (brush != IntPtr.Zero && GetClientRect(_hwnd, out var client)) FillRect(hdc, ref client, brush);
        return true;
    }

    // The band is gone or the host stops: the strip goes at once, at no
    // opacity first so that nothing of Windows' closing is seen.
    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero && IsWindow(_hwnd))
        {
            NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA);
            NativeMethods.DestroyWindow(_hwnd);
        }
        _hwnd = IntPtr.Zero;
        if (_brush != IntPtr.Zero) DeleteObject(_brush);
        _brush = IntPtr.Zero;
        App = IntPtr.Zero;
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
            return SetWindowCompositionAttribute(_hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    // ── Reading the windows ───────────────────────────────────────────────

    // The topmost maximized app on monitor, on the current virtual desktop;
    // zero when there is none, or when a window in front of it covers the
    // whole monitor (a video or a game fullscreen: no taskbar beside it to
    // continue). Measured against the frame Windows draws
    // (DWMWA_EXTENDED_FRAME_BOUNDS), since a maximized window's rectangle
    // overhangs the monitor by its resize borders. Always-on-top windows are
    // left out: the underlay placed under one would become always-on-top
    // itself. Tool windows and windows that take no focus are left out of
    // the fullscreen test: such a window covering the monitor is an overlay,
    // not an app.
    public static IntPtr FindMaximizedApp(IntPtr monitor, NativeMethods.RECT monitorRect)
    {
        for (IntPtr w = GetTopWindow(IntPtr.Zero); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDNEXT))
        {
            if (!IsWindowVisible(w)) continue;
            long ex = NativeMethods.GetWindowLongPtr(w, GWL_EXSTYLE).ToInt64();
            if ((ex & NativeMethods.WS_EX_TOPMOST) != 0) continue;
            if (DwmGetWindowAttribute(w, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            if (MonitorFromWindow(w, MONITOR_DEFAULTTONEAREST) != monitor) continue;
            if (IsZoomed(w)) return CoversMonitor(w, monitorRect) ? IntPtr.Zero : w;
            if ((ex & (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE)) == 0 && CoversMonitor(w, monitorRect))
                return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    private static bool CoversMonitor(IntPtr w, NativeMethods.RECT m)
        => DwmGetWindowAttribute(w, DWMWA_EXTENDED_FRAME_BOUNDS, out NativeMethods.RECT r, Marshal.SizeOf<NativeMethods.RECT>()) == 0
           && r.left <= m.left && r.top <= m.top && r.right >= m.right && r.bottom >= m.bottom;

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

    // Task view holds the foreground: its desktop previews would show the
    // strip as a bar beside the app with no taskbar over it. Alt+Tab's
    // switcher is a window of the same class with the same styles (measured
    // 2026-11-27); only its title, which Windows translates, tells it apart.
    public static bool TaskViewShown()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        var text = new System.Text.StringBuilder(64);
        if (foreground == IntPtr.Zero || GetClassName(foreground, text, text.Capacity) == 0
            || text.ToString() != TaskViewClass)
            return false;
        text.Clear();
        GetWindowText(foreground, text, text.Capacity);
        return text.ToString() != TaskSwitcherTitle;
    }

    // The colour a strip starts from before the app's first frame is read:
    // the base of Windows 11 apps in the apps' theme, #202020 dark and
    // #F3F3F3 light (WinUI's SolidBackgroundFillColorBase), as a COLORREF.
    public static uint ThemeBase()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        bool light = key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        return light ? 0x00F3F3F3u : 0x00202020u;
    }
}

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
// One motion only. A strip is a window that Windows never shows, hides nor
// animates once it is there: it changes its opacity, its colour and its blur
// through one fade (UnderlayFade), and it is made and destroyed only at no
// opacity, where nothing of Windows' opening or closing is seen. Windows
// hides an owned window itself when its owner is minimized, or when another
// window is maximized, through WM_SHOWWINDOW; the host refuses those
// (TaskbarCoverHost's window procedure), and the strip fades out as its app
// leaves. A strip made and destroyed per change, at full opacity, showed
// Windows' own animations under the fade (Louis, trial of v0.159.0; the
// minimize measured on 2026-11-27: the strip gone in one frame).
//
// One strip per app shown, owned by the app. An owned window lives on its
// owner's virtual desktop, so a switch of desktop slides the strip away with
// its app, and back with it: a strip whose app is still maximized on a
// desktop not shown stays there at the opacity it had. A strip of no owner
// would show on every desktop, and Windows reads the owner at creation only,
// so a new app gets a new strip. Windows destroys an owned window with its
// owner (an app closed while maximized takes its strip at once); IsWindow
// catches it.
//
// Two apps in turn (one maximized over the other, Alt+Tab between them):
// the new one's strip fades in over the old one's, which fades out under it,
// each from where it stands. Without animations every change is instant.
// The curve and duration are UnderlayMotion's. Worker thread only, like the
// band that owns it.
internal sealed class TaskbarUnderlay : IDisposable
{
    // How much of the edge colour tints the blur: enough to carry the app's
    // colour, little enough to let the blurred wallpaper through, as the
    // app's own acrylic does. 255, the plain strip, is the tint at its full.
    private const byte BlurTint = 0x99;
    private const byte PlainTint = 0xFF;

    private readonly string _className;
    private readonly IntPtr _hInstance;
    private readonly List<Strip> _strips = [];
    // Windows refused the blur once: the strips stay plain.
    private bool _blurRefused;

    // The maximized app the underlay continues; zero while hidden, fading
    // out included.
    public IntPtr App { get; private set; }
    public bool Visible => App != IntPtr.Zero;
    // The COLORREF last asked for.
    public uint Color { get; private set; } = uint.MaxValue;
    // The last read edge showed more than one colour (EdgeColor.PlainShare).
    public bool EdgeVaried { get; set; }
    // A fade is under way: the host ticks it.
    public bool Animating => _strips.Exists(s => s.Fade.Moving);

    public TaskbarUnderlay(string className, IntPtr hInstance)
    {
        _className = className;
        _hInstance = hInstance;
    }

    // The one way the strips change. app's strip shows over rect in color (a
    // COLORREF), plain or as a blur tinted with it; app zero shows none. Every
    // other strip leaves, but for one whose app is still maximized on a
    // desktop not shown, which keeps its opacity unless desktopsShown is
    // false (Task view shows the desktops' windows without their taskbar).
    // Fades when animate. Returns 0, or the Win32 error of a window Windows
    // refused, that strip then staying hidden.
    public int Update(IntPtr app, NativeMethods.RECT rect, uint color, bool blur, bool animate, bool desktopsShown)
    {
        int error = 0;
        if (app != IntPtr.Zero && _strips.Find(s => s.Owner == app) is null)
        {
            var created = Strip.Create(_className, _hInstance, app, out error);
            if (created is null) app = IntPtr.Zero;
            else _strips.Add(created);
        }
        App = app;
        if (app != IntPtr.Zero) Color = color;

        long now = Environment.TickCount64;
        for (int i = _strips.Count - 1; i >= 0; i--)
        {
            var strip = _strips[i];
            if (!IsWindow(strip.Hwnd))
            {
                strip.Release();
                _strips.RemoveAt(i);
                continue;
            }

            UnderlayLook target;
            bool keep;
            if (strip.Owner == app)
            {
                strip.Parked = null;
                strip.Place(rect);
                target = new UnderlayLook(255, color, blur && !_blurRefused ? BlurTint : PlainTint);
                keep = true;
            }
            else if (OnHiddenDesktop(strip.Owner))
            {
                // Left there as it was when its app went.
                strip.Parked ??= strip.Fade.Shown.Alpha > 0;
                target = strip.Fade.Target with { Alpha = strip.Parked.Value && desktopsShown ? (byte)255 : (byte)0 };
                keep = true;
            }
            else
            {
                strip.Parked = null;
                target = strip.Fade.Target with { Alpha = 0 };
                keep = false;
            }

            strip.Fade.MoveTo(target, animate, now);
            if (!strip.Paint()) _blurRefused = true;
            if (!keep && strip.Gone) Drop(i);
        }
        return error;
    }

    // One frame of the fades, at now (TickCount64). A strip that has faded
    // away for good goes, unseen.
    public void Tick(long now)
    {
        for (int i = _strips.Count - 1; i >= 0; i--)
        {
            var strip = _strips[i];
            strip.Fade.Tick(now);
            strip.Paint();
            if (strip.Gone && strip.Owner != App && strip.Parked is null) Drop(i);
        }
    }

    private void Drop(int i)
    {
        _strips[i].Destroy();
        _strips.RemoveAt(i);
    }

    private static bool OnHiddenDesktop(IntPtr app)
        => IsWindow(app) && IsZoomed(app) && !IsIconic(app)
           && DwmGetWindowAttribute(app, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
           && (cloaked & DWM_CLOAKED_SHELL) != 0;

    // WM_ERASEBKGND for one of the strips; false when hwnd is none of them.
    public bool TryPaint(IntPtr hwnd, IntPtr hdc)
    {
        var strip = _strips.Find(s => s.Hwnd == hwnd);
        if (strip is null) return false;
        strip.Erase(hdc);
        return true;
    }

    // The band is gone or the host stops: the strips go at once, at no
    // opacity first.
    public void Dispose()
    {
        foreach (var strip in _strips) strip.Destroy();
        _strips.Clear();
        App = IntPtr.Zero;
    }

    // One window of the underlay, owned by its app, with its fade.
    private sealed class Strip
    {
        public IntPtr Hwnd { get; private init; }
        public IntPtr Owner { get; private init; }
        public UnderlayFade Fade { get; } = new();
        // Set while the app is maximized on a desktop not shown: whether the
        // strip showed when its app went there.
        public bool? Parked { get; set; }
        // Faded away and still: it can go unseen.
        public bool Gone => Fade.Shown.Alpha == 0 && !Fade.Moving;
        private UnderlayLook _painted;
        private bool _accentOn;
        private IntPtr _brush;
        private NativeMethods.RECT _rect;

        // Made at no opacity and shown once, for good: nothing of Windows'
        // opening is seen.
        public static Strip? Create(string className, IntPtr hInstance, IntPtr owner, out int error)
        {
            // NOACTIVATE + TOOLWINDOW like the anchor: never focused, never in
            // Alt-Tab. LAYERED for the fade, TRANSPARENT so that it takes no
            // click.
            IntPtr hwnd = NativeMethods.CreateWindowEx(
                dwExStyle:    NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE
                            | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT,
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
            uint noTransitions = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref noTransitions, sizeof(uint));
            // A layered window shows nothing until its alpha is set.
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 0, NativeMethods.LWA_ALPHA);
            return new Strip { Hwnd = hwnd, Owner = owner };
        }

        // Over r, where it is in the z-order: an owned window stays above
        // its owner on its own, and placing it against another app's window
        // would raise its owner with it (Louis, trial of 2026-11-27). The
        // first call shows the window, for good.
        public void Place(NativeMethods.RECT r)
        {
            if (r.left == _rect.left && r.top == _rect.top && r.right == _rect.right && r.bottom == _rect.bottom) return;
            _rect = r;
            NativeMethods.SetWindowPos(Hwnd, IntPtr.Zero, r.left, r.top, r.right - r.left, r.bottom - r.top,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        }

        // Writes what the fade shows now: the opacity, the colour, the blur.
        // False when Windows refused the blur, the strip then plain.
        public bool Paint()
        {
            var look = Fade.Shown;
            if (look == _painted && _brush != IntPtr.Zero) return true;
            bool accepted = true;
            if (look.Color != _painted.Color || _brush == IntPtr.Zero)
            {
                if (_brush != IntPtr.Zero) DeleteObject(_brush);
                _brush = CreateSolidBrush(look.Color);
            }
            bool accent = look.Tint < PlainTint;
            if (accent && (!_accentOn || look.Tint != _painted.Tint || look.Color != _painted.Color))
            {
                _accentOn = SetAccent(ACCENT_ENABLE_ACRYLICBLURBEHIND, (uint)look.Tint << 24 | look.Color);
                accepted = _accentOn;
            }
            else if (!accent && _accentOn)
            {
                SetAccent(ACCENT_DISABLED, 0);
                _accentOn = false;
            }
            if (look.Color != _painted.Color || accent != (_painted.Tint < PlainTint) || _painted.Alpha == 0)
                InvalidateRect(Hwnd, IntPtr.Zero, true);
            if (look.Alpha != _painted.Alpha)
                NativeMethods.SetLayeredWindowAttributes(Hwnd, 0, look.Alpha, NativeMethods.LWA_ALPHA);
            _painted = look;
            return accepted;
        }

        // WM_ERASEBKGND: the whole strip in one colour, or black under the
        // blur, which the accent draws as see-through.
        public void Erase(IntPtr hdc)
        {
            IntPtr brush = _accentOn ? GetStockObject(BLACK_BRUSH) : _brush;
            if (brush != IntPtr.Zero && GetClientRect(Hwnd, out var client)) FillRect(hdc, ref client, brush);
        }

        public void Destroy()
        {
            if (IsWindow(Hwnd))
            {
                NativeMethods.SetLayeredWindowAttributes(Hwnd, 0, 0, NativeMethods.LWA_ALPHA);
                NativeMethods.DestroyWindow(Hwnd);
            }
            Release();
        }

        // The window is gone already: only its brush is left.
        public void Release()
        {
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

    // Task view while it covers the screen: known, the Task view window
    // found before, as long as Windows shows it (it stays over the screen
    // while it closes, after the app chosen took the foreground), else the
    // foreground when it is Task view; zero otherwise. Alt+Tab's switcher is
    // a window of the same class, styles and size; its title differs but
    // Windows translates it. What tells them apart is the z-order band
    // Windows puts each in: the switcher sits in the system tools band,
    // Task view does not (measured 2026-11-27: bands 16 and 5).
    // GetWindowBand is undocumented: where it is missing, nothing reads as
    // Task view and the strip stays through it.
    public static IntPtr FindTaskView(IntPtr known)
    {
        if (known != IntPtr.Zero && IsWindow(known) && IsWindowVisible(known)) return known;
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        var text = new System.Text.StringBuilder(64);
        if (foreground == IntPtr.Zero || GetClassName(foreground, text, text.Capacity) == 0
            || text.ToString() != TaskViewClass)
            return IntPtr.Zero;
        try
        {
            return GetWindowBand(foreground, out uint band) && band != ZBID_SYSTEM_TOOLS ? foreground : IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
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

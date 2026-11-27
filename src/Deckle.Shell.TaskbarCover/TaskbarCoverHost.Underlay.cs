using Deckle.Vision;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── Underlay — the colour behind a faded taskbar ──────────────────────
    //
    // One state, one motion. A band's strip is there while a maximized app
    // is on its monitor, and gone otherwise. It dissolves in, out and from
    // one colour to the next, always the same fade (TaskbarUnderlay,
    // UnderlayMotion).
    //
    // Which app: the topmost maximized window on the monitor, on the virtual
    // desktop shown (TaskbarUnderlay.FindMaximizedApp walks the z-order).
    // That rule needs no case of its own for Alt+Tab, for a virtual-desktop
    // switch or for a window opened over the app. Alt+Tab's switcher is not
    // a maximized app, so nothing changes until a window is chosen. A switch
    // of desktop cloaks the old desktop's windows. A normal window over the
    // app leaves the app below (measured 2026-11-27). A window that covers
    // the whole monitor (a video or a game fullscreen) is not an app beside
    // the taskbar, and takes the strip away; so does the foreground's
    // fullscreen verdict (the band's suppression), which also catches what
    // the walk leaves out, such as an always-on-top game.
    //
    // One strip per taskbar, made once, on every virtual desktop like the
    // taskbar: a switch of desktop leaves it under the taskbar, and it
    // fades to the next desktop's app or away (TaskbarUnderlay).
    //
    // Task view takes the strip away: it reads as the desktop. Windows
    // describes it as it describes Alt+Tab's switcher (same class, same
    // styles, measured 2026-11-27), and only the title tells them apart. It
    // is the one rule that leans on a title, which Windows translates.
    //
    // Which colour: the app's own edge, read from the frames the app draws
    // (WindowEdgeReader), never from the screen. The screen shows whatever is
    // over the edge, the wallpaper during Windows' maximize animation or a
    // flyout sliding past, and reading it meant waiting and comparing. The
    // app's frame is the app's colour at every frame. A strip that appears
    // starts from the theme's base colour, then dissolves to the edge's as
    // soon as the first frame is read: the strip never waits to appear.
    // When the theme changes, the strip goes to the new base at once and
    // follows the app as it repaints.
    //
    // When it looks: a top-level window that moves, is maximized, restored,
    // minimized, cloaked or uncloaked (the location hook), at most once per
    // UnderlaySoonMs; the foreground; the layout; sleep and lock.
    //
    // The strip dissolves when Deckle's animations are on (the preference the
    // App passes, which follows Windows' « Animation effects » by default),
    // and changes at once when they are off. Its fades step once per frame of
    // the display, on a frame clock (WM_APP_UNDERLAY_FRAME).
    //
    // An app that shows Mica or Acrylic gets a blur tinted with its edge
    // colour instead of the plain colour, while it shows it: the focus moves
    // through the foreground trigger, and the strip turns plain with the app.

    private static readonly UIntPtr UnderlaySoonTimer = 2;
    // One look per frame at 60 Hz at most, however many windows move.
    private const uint UnderlaySoonMs = 16;

    private bool _underlaySoonPending;
    private bool _underlayFailureLogged;
    // The frame clock's generation: a clock thread runs while its number is
    // the current one, so stopping it is a bump and a new one never meets
    // the old.
    private int _underlayFrameClock;
    private bool _underlayMotionRunning;
    // Each edge reader started gets a number, so a colour posted by a reader
    // since replaced is recognised and dropped.
    private int _edgeReaderGeneration;

    // From the hooks: at most one look per UnderlaySoonMs.
    private void ScheduleUnderlayRefresh()
    {
        if (_underlaySoonPending) return;
        _underlaySoonPending = true;
        SetTimer(_hwnd, UnderlaySoonTimer, UnderlaySoonMs, IntPtr.Zero);
    }

    private void OnUnderlayTimer(UIntPtr id)
    {
        if (id != UnderlaySoonTimer) return;
        KillTimer(_hwnd, UnderlaySoonTimer);
        _underlaySoonPending = false;
        RefreshUnderlays("window_moved");
    }

    // WM_APP_UNDERLAY_FRAME: the display showed a frame.
    private void OnUnderlayFrame()
    {
        long now = Environment.TickCount64;
        foreach (var band in _bands) band.Underlay?.Tick(now);
        KeepUnderlayMotion();
    }

    private void RefreshUnderlays(string reason)
    {
        bool taskView = TaskbarUnderlay.TaskViewShown();
        foreach (var band in _bands) RefreshUnderlay(band, reason, taskView);
        KeepUnderlayMotion();
    }

    // Runs the frame clock while a strip fades, and only then.
    private void KeepUnderlayMotion()
    {
        bool moving = _bands.Any(b => b.Underlay is { Animating: true });
        if (moving == _underlayMotionRunning) return;
        _underlayMotionRunning = moving;
        int generation = Interlocked.Increment(ref _underlayFrameClock);
        if (moving) StartFrameClock(_hwnd, generation);
    }

    private void StopUnderlayMotion()
    {
        _underlayMotionRunning = false;
        Interlocked.Increment(ref _underlayFrameClock);
    }

    // Posts one frame message per frame the compositor shows, until the
    // generation moves on. A DwmFlush refused (no composition) falls back
    // to an 8 ms sleep, a frame at 120 Hz.
    private void StartFrameClock(IntPtr hwnd, int generation)
    {
        var clock = new Thread(() =>
        {
            while (Volatile.Read(ref _underlayFrameClock) == generation)
            {
                if (DwmFlush() != 0) Thread.Sleep(8);
                if (Volatile.Read(ref _underlayFrameClock) != generation) break;
                PostMessage(hwnd, WM_APP_UNDERLAY_FRAME, IntPtr.Zero, IntPtr.Zero);
            }
        })
        {
            IsBackground = true,
            Name = "Deckle.TaskbarCover.UnderlayFrames",
        };
        clock.Start();
    }

    private void RefreshUnderlay(CoverBand band, string reason, bool taskView)
    {
        bool possible = _layoutKnown && !_systemSuspended && !band.AppSuppressed && !taskView;
        IntPtr app = possible ? TaskbarUnderlay.FindMaximizedApp(band.Monitor, band.MonitorRect) : IntPtr.Zero;
        bool animate = possible && _animationsEnabled();

        if (app == IntPtr.Zero)
        {
            StopEdgeReader(band);
            if (band.Underlay is { Visible: true } shown)
            {
                shown.Hide(animate);
                DeckleShellTaskbarCoverSource.Log.UnderlayHidden(band.Index, reason);
            }
            return;
        }

        var underlay = band.Underlay ??= new TaskbarUnderlay(ClassName, _hInstance);
        bool wasVisible = underlay.Visible;
        if (app != band.EdgeReaderApp) StartEdgeReader(band, app);

        // A strip that appears starts from the theme's base, and one that
        // passes to another app keeps its colour: either way the first frame
        // read takes it to the app's edge.
        uint color = wasVisible ? underlay.Color : TaskbarUnderlay.ThemeBase();
        bool blur = wasVisible && underlay.EdgeVaried && TaskbarUnderlay.ShowsBackdrop(app);
        ShowUnderlay(band, underlay, app, color, blur, animate);
        if (!wasVisible && underlay.Visible) DeckleShellTaskbarCoverSource.Log.UnderlayShown(band.Index, reason);
    }

    private void ShowUnderlay(CoverBand band, TaskbarUnderlay underlay, IntPtr app, uint color, bool blur, bool animate)
    {
        int error = underlay.Show(app, band.Rect, color, blur, animate);
        if (error != 0)
        {
            if (!_underlayFailureLogged)
            {
                DeckleShellTaskbarCoverSource.Log.UnderlayFailed(band.Index, error);
                _underlayFailureLogged = true;
            }
            return;
        }
        if (_underlayFailureLogged)
        {
            DeckleShellTaskbarCoverSource.Log.UnderlayRecovered();
            _underlayFailureLogged = false;
        }
    }

    // ── The app's edge ────────────────────────────────────────────────────

    // Reads app's edge beside the band's taskbar. A colour is posted only
    // when it differs from the last one this reader posted: the app sends a
    // frame each time it draws anything, and most frames leave its edge as
    // it was.
    private void StartEdgeReader(CoverBand band, IntPtr app)
    {
        StopEdgeReader(band);
        band.EdgeReaderApp = app;
        int generation = ++_edgeReaderGeneration & 0xFFFF;
        band.EdgeReaderGeneration = generation;
        var side = band.Edge switch
        {
            TaskbarEdge.Left  => WindowEdgeReader.Side.Left,
            TaskbarEdge.Top   => WindowEdgeReader.Side.Top,
            TaskbarEdge.Right => WindowEdgeReader.Side.Right,
            _                 => WindowEdgeReader.Side.Bottom,
        };
        IntPtr hwnd = _hwnd;
        IntPtr where = (IntPtr)((band.Index << 16) | generation);
        long last = -1;
        try
        {
            band.EdgeReader = WindowEdgeReader.Start(app, side, pixels =>
            {
                uint color = EdgeColor.Dominant(pixels, out double share);
                long packed = color | (share < EdgeColor.PlainShare ? 1L << 24 : 0);
                if (packed == last) return;
                last = packed;
                PostMessage(hwnd, WM_APP_UNDERLAY_EDGE, (IntPtr)packed, where);
            });
        }
        catch (Exception ex)
        {
            // The strip stays at the colour it shows: a window that refuses
            // the capture is rare (one gone already), and the next look
            // tries again with the next app.
            DeckleShellTaskbarCoverSource.Log.UnderlayEdgeReadFailed(band.Index, ex.GetType().Name);
        }
    }

    private static void StopEdgeReader(CoverBand band)
    {
        band.EdgeReader?.Dispose();
        band.EdgeReader = null;
        band.EdgeReaderApp = IntPtr.Zero;
    }

    // WM_APP_UNDERLAY_EDGE: a reader saw the app's edge change colour.
    private void OnUnderlayEdge(IntPtr wParam, IntPtr lParam)
    {
        int index = (int)((long)lParam >> 16), generation = (int)((long)lParam & 0xFFFF);
        var band = _bands.FirstOrDefault(b => b.Index == index);
        if (band is null || band.EdgeReaderGeneration != generation) return;
        if (band.Underlay is not { Visible: true } underlay || underlay.App != band.EdgeReaderApp) return;

        long packed = (long)wParam;
        uint color = (uint)(packed & 0xFFFFFF);
        underlay.EdgeVaried = (packed >> 24 & 1) != 0;
        // The blur only where the app's edge really shows through: an app can
        // ask for Mica and paint its own opaque colour over it (T3 Code asks
        // for Mica, type 2, and draws black), and a blur beside a plain edge
        // shows the wallpaper the app hides.
        bool blur = underlay.EdgeVaried && TaskbarUnderlay.ShowsBackdrop(underlay.App);
        ShowUnderlay(band, underlay, underlay.App, color, blur, _animationsEnabled());
        KeepUnderlayMotion();
    }

    // The light or dark theme changed: every strip goes to the new theme's
    // base at once, and its reader starts again so that the app's first
    // frame in the new theme is posted even if it matches a colour posted
    // before.
    private void OnThemeChanged()
    {
        uint color = TaskbarUnderlay.ThemeBase();
        foreach (var band in _bands)
        {
            if (band.Underlay is not { Visible: true } underlay) continue;
            ShowUnderlay(band, underlay, underlay.App, color, blur: false, _animationsEnabled());
            StartEdgeReader(band, underlay.App);
        }
        KeepUnderlayMotion();
    }

    private static void DisposeUnderlay(CoverBand band)
    {
        StopEdgeReader(band);
        band.Underlay?.Dispose();
        band.Underlay = null;
    }

    // WM_ERASEBKGND for a window of the class other than the anchor.
    private bool TryPaintUnderlay(IntPtr hwnd, IntPtr hdc)
    {
        foreach (var band in _bands)
            if (band.Underlay?.TryPaint(hwnd, hdc) == true) return true;
        return false;
    }
}

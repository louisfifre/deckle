using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── Underlay — the colour behind a faded taskbar ──────────────────────
    //
    // While an app is maximized on a band's monitor, the band's underlay
    // fills the taskbar's place with the colour of the app's edge
    // (TaskbarUnderlay). Without one, the faded taskbar shows the wallpaper,
    // as it should between floating windows.
    //
    // Triggers: the layout, the foreground, sleep and lock; a top-level
    // window that moves, is maximized, restored or minimized (the location
    // hook, throttled to one look per UnderlaySoonMs); and a slow period that
    // follows a colour change inside the app. The edge is read only when the
    // app changes and on the period: a dragged window costs a z-order walk,
    // never a capture (one read is about 8 ms, measured, the screen read
    // waiting on the compositor).
    //
    // A new app is shown once Windows' maximize animation has brought it to
    // the edge: during the animation the edge still shows what was there,
    // and a read then painted the strip the wallpaper's colour for a third
    // of a second (seen in the trial). So the edge is read when the app is
    // found, and the app is shown when the edge no longer shows that colour,
    // or after UnderlayAnimationMs for an app whose edge is the same colour.
    // Until then the strip keeps its state, which matches the screen.
    //
    // The edge is read only while the app itself shows along it: a flyout
    // opened over it (Windows + N slides the notification centre in from the
    // bar's side) would lend the strip its colour. And no read happens for
    // UnderlayHoldMs after the foreground changes or a covered edge is seen:
    // a flyout that closes slides out over the edge, still painted after it
    // stopped being the window there. Settle reads cover the end of the hold.
    //
    // Task view hides every strip: its desktop previews show no taskbar, and
    // a strip there reads as a stray bar.
    //
    // The strip moves when Deckle's animations are on (the preference the App
    // passes, which follows Windows' « Animation effects » by default), and
    // changes at once when they are off (TaskbarUnderlay). Task view, sleep
    // and a lost layout take it away at once: nothing moves there to carry on.
    //
    // An app that shows Mica or Acrylic gets a blur tinted with its edge
    // colour instead of the plain colour, while it shows it: the focus moves
    // through the foreground trigger, and the strip turns plain with the app.

    private static readonly UIntPtr UnderlayPeriodTimer = 1;
    private static readonly UIntPtr UnderlaySoonTimer   = 2;
    private static readonly UIntPtr UnderlaySettleTimer = 3;
    private static readonly UIntPtr UnderlayMotionTimer = 4;
    private const uint UnderlayPeriodMs = 500;
    private const uint UnderlaySoonMs   = 30;
    private const uint UnderlaySettleMs = 50;
    private const int  UnderlaySettleReads = 12;
    // One frame of the strip's motion; Windows rounds a timer up to its
    // clock tick, about 16 ms, about one frame at 60 Hz.
    private const uint UnderlayMotionMs = 10;
    private const long UnderlayAnimationMs = 600;
    private const long UnderlayHoldMs = 400;

    private bool _underlaySoonPending;
    private long _underlayHoldUntil;
    private int _underlaySettleReadsLeft;
    private bool _underlayFailureLogged;
    private bool _underlayMotionRunning;

    private void StartUnderlayPeriod() => SetTimer(_hwnd, UnderlayPeriodTimer, UnderlayPeriodMs, IntPtr.Zero);

    // From the location hook: at most one look per UnderlaySoonMs.
    private void ScheduleUnderlayRefresh()
    {
        if (_underlaySoonPending) return;
        _underlaySoonPending = true;
        SetTimer(_hwnd, UnderlaySoonTimer, UnderlaySoonMs, IntPtr.Zero);
    }

    private void OnUnderlayTimer(UIntPtr id)
    {
        if (id == UnderlaySoonTimer)
        {
            KillTimer(_hwnd, UnderlaySoonTimer);
            _underlaySoonPending = false;
            RefreshUnderlays("window_moved", readColor: false);
        }
        else if (id == UnderlaySettleTimer)
        {
            if (--_underlaySettleReadsLeft <= 0) KillTimer(_hwnd, UnderlaySettleTimer);
            RefreshUnderlays("settle", readColor: true);
        }
        else if (id == UnderlayPeriodTimer)
        {
            RefreshUnderlays("period", readColor: true);
        }
        else if (id == UnderlayMotionTimer)
        {
            long now = Environment.TickCount64;
            foreach (var band in _bands) band.Underlay?.Tick(now);
            KeepUnderlayMotion();
        }
    }

    // The foreground changed: hold the reads through the flyout and window
    // animations it starts, and read again once they are over.
    private void HoldUnderlayReads()
    {
        _underlayHoldUntil = Environment.TickCount64 + UnderlayHoldMs;
        _underlaySettleReadsLeft = UnderlaySettleReads;
        SetTimer(_hwnd, UnderlaySettleTimer, UnderlaySettleMs, IntPtr.Zero);
    }

    // The colour along the band's edge, or null while it cannot be trusted:
    // held, covered by another window, or refused by GDI.
    private (uint Color, double Share)? ReadEdge(CoverBand band, IntPtr app)
    {
        if (Environment.TickCount64 < _underlayHoldUntil) return null;
        var line = EdgeColor.SampleLine(band.Rect, band.Edge);
        if (!TaskbarUnderlay.AppOwnsLine(app, line))
        {
            _underlayHoldUntil = Environment.TickCount64 + UnderlayHoldMs;
            return null;
        }
        return TaskbarUnderlay.ReadColor(line);
    }

    private void RefreshUnderlays(string reason, bool readColor = true)
    {
        foreach (var band in _bands) RefreshUnderlay(band, reason, readColor);
        KeepUnderlayMotion();
    }

    // Runs the motion timer while a strip moves, and only then.
    private void KeepUnderlayMotion()
    {
        bool moving = _bands.Any(b => b.Underlay is { Animating: true });
        if (moving == _underlayMotionRunning) return;
        _underlayMotionRunning = moving;
        if (moving) SetTimer(_hwnd, UnderlayMotionTimer, UnderlayMotionMs, IntPtr.Zero);
        else        KillTimer(_hwnd, UnderlayMotionTimer);
    }

    private void RefreshUnderlay(CoverBand band, string reason, bool readColor)
    {
        bool possible = _layoutKnown && !_systemSuspended && !TaskbarUnderlay.TaskViewShown();
        IntPtr app = possible ? TaskbarUnderlay.FindMaximizedApp(band.Monitor) : IntPtr.Zero;
        bool animate = possible && _animationsEnabled();

        if (app == IntPtr.Zero)
        {
            if (band.Underlay is { Visible: true } shown)
            {
                shown.Hide(animate);
                DeckleShellTaskbarCoverSource.Log.UnderlayHidden(band.Index, reason);
            }
            return;
        }

        var underlay = band.Underlay ??= new TaskbarUnderlay(ClassName, _hInstance);

        bool appChanged = app != underlay.App;
        if (appChanged)
        {
            if (underlay.PendingApp != app)
            {
                underlay.PendingApp = app;
                underlay.PendingSince = Environment.TickCount64;
                underlay.PendingBefore = TaskbarUnderlay.ReadColor(EdgeColor.SampleLine(band.Rect, band.Edge))?.Color;
                _underlaySettleReadsLeft = UnderlaySettleReads;
                SetTimer(_hwnd, UnderlaySettleTimer, UnderlaySettleMs, IntPtr.Zero);
                return;
            }
        }
        var reading = appChanged || readColor ? ReadEdge(band, app) : null;
        uint? color = reading?.Color;
        if (appChanged && color is { } seen && underlay.PendingBefore is { } before
            && EdgeColor.Close(seen, before)
            && Environment.TickCount64 - underlay.PendingSince < UnderlayAnimationMs)
            return;
        // A failed read keeps the colour already shown; a new app with no
        // colour yet waits for the next trigger rather than show a guess.
        if (color is null && (appChanged || !underlay.Visible)) return;
        underlay.PendingApp = IntPtr.Zero;
        if (reading is { } r) underlay.EdgeVaried = r.Share < EdgeColor.PlainShare;

        // The blur only where the app's edge really shows through: an app can
        // ask for Mica and paint its own opaque colour over it (T3 Code asks
        // for Mica, type 2, and draws black), and a blur beside a plain edge
        // shows the wallpaper the app hides.
        bool blur = underlay.EdgeVaried && TaskbarUnderlay.ShowsBackdrop(app);
        bool wasVisible = underlay.Visible;
        int error = underlay.Show(app, band.Rect, band.Edge, color ?? underlay.Color, blur, animate);
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
        if (!wasVisible) DeckleShellTaskbarCoverSource.Log.UnderlayShown(band.Index, reason);
    }

    private static void DisposeUnderlay(CoverBand band)
    {
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

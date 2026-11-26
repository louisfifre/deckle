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
    // An app that shows Mica or Acrylic gets a blur tinted with its edge
    // colour instead of the plain colour, while it shows it: the focus moves
    // through the foreground trigger, and the strip turns plain with the app.

    private static readonly UIntPtr UnderlayPeriodTimer = 1;
    private static readonly UIntPtr UnderlaySoonTimer   = 2;
    private static readonly UIntPtr UnderlaySettleTimer = 3;
    private const uint UnderlayPeriodMs = 500;
    private const uint UnderlaySoonMs   = 30;
    private const uint UnderlaySettleMs = 50;
    private const int  UnderlaySettleReads = 12;
    private const long UnderlayAnimationMs = 600;

    private bool _underlaySoonPending;
    private int _underlaySettleReadsLeft;
    private bool _underlayFailureLogged;

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
    }

    private void RefreshUnderlays(string reason, bool readColor = true)
    {
        foreach (var band in _bands) RefreshUnderlay(band, reason, readColor);
    }

    private void RefreshUnderlay(CoverBand band, string reason, bool readColor)
    {
        IntPtr app = _layoutKnown && !_systemSuspended
            ? TaskbarUnderlay.FindMaximizedApp(band.Monitor)
            : IntPtr.Zero;

        if (app == IntPtr.Zero)
        {
            if (band.Underlay is { Visible: true } shown)
            {
                shown.Hide();
                DeckleShellTaskbarCoverSource.Log.UnderlayHidden(band.Index, reason);
            }
            return;
        }

        var underlay = band.Underlay ??= CreateUnderlay(band);
        if (underlay is null) return;

        bool appChanged = app != underlay.App;
        if (appChanged)
        {
            if (underlay.PendingApp != app)
            {
                underlay.PendingApp = app;
                underlay.PendingSince = Environment.TickCount64;
                underlay.PendingBefore = TaskbarUnderlay.ReadColor(EdgeColor.SampleLine(band.Rect, band.Edge));
                _underlaySettleReadsLeft = UnderlaySettleReads;
                SetTimer(_hwnd, UnderlaySettleTimer, UnderlaySettleMs, IntPtr.Zero);
                return;
            }
        }
        uint? color = appChanged || readColor
            ? TaskbarUnderlay.ReadColor(EdgeColor.SampleLine(band.Rect, band.Edge))
            : null;
        if (appChanged && color is { } seen && underlay.PendingBefore is { } before
            && EdgeColor.Close(seen, before)
            && Environment.TickCount64 - underlay.PendingSince < UnderlayAnimationMs)
            return;
        // A failed read keeps the colour already shown; a new app with no
        // colour yet waits for the next trigger rather than show a guess.
        if (color is null && (appChanged || !underlay.Visible)) return;
        underlay.PendingApp = IntPtr.Zero;

        bool wasVisible = underlay.Visible;
        underlay.Show(app, band.Rect, color ?? underlay.Color, TaskbarUnderlay.ShowsBackdrop(app));
        if (!wasVisible) DeckleShellTaskbarCoverSource.Log.UnderlayShown(band.Index, reason);
    }

    private TaskbarUnderlay? CreateUnderlay(CoverBand band)
    {
        var underlay = TaskbarUnderlay.Create(ClassName, _hInstance, out int error);
        if (underlay is null)
        {
            if (!_underlayFailureLogged)
            {
                DeckleShellTaskbarCoverSource.Log.UnderlayFailed(band.Index, error);
                _underlayFailureLogged = true;
            }
            return null;
        }
        if (_underlayFailureLogged)
        {
            DeckleShellTaskbarCoverSource.Log.UnderlayRecovered();
            _underlayFailureLogged = false;
        }
        return underlay;
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
        {
            if (band.Underlay?.Hwnd != hwnd) continue;
            band.Underlay.Paint(hdc);
            return true;
        }
        return false;
    }
}

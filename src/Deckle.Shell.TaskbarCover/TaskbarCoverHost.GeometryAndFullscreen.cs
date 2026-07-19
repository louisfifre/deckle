using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // Another app came to the foreground: track it (the location-change hook
    // compares against it to catch an in-place F11 resize), reconcile
    // suppression, and — while the band is up — climb back above the taskbar,
    // which Explorer re-asserts topmost on fullscreen exit.
    private void OnForegroundEvent(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (idObject != OBJID_WINDOW) return;
        if (_systemSuspended || !_layoutKnown) return;

        _foregroundHwnd = hwnd;
        EvaluateAppSuppressed();
        if (_coverVisible) ReassertTopmost();
    }

    // ── Geometry ──────────────────────────────────────────────────────────

    private void RebuildLayout(string reason)
    {
        var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) == IntPtr.Zero)
        {
            // No fabricated fallback: a wrong band is worse than no band.
            // The cover stays hidden until TaskbarCreated or the retry tick
            // finds the taskbar.
            if (!_layoutFailureLogged)
            {
                DeckleShellTaskbarCoverSource.Log.LayoutQueryFailed();
                _layoutFailureLogged = true;
            }
            _layoutKnown = false;
            UpdateCover("layout_unknown");
            return;
        }
        if (_layoutFailureLogged)
            DeckleShellTaskbarCoverSource.Log.LayoutQueryRecovered();
        _layoutFailureLogged = false;

        var edge = (TaskbarEdge)abd.uEdge;
        var band = abd.rc;
        bool changed = !_layoutKnown || edge != _edge
            || band.left != _band.left || band.top != _band.top
            || band.right != _band.right || band.bottom != _band.bottom;

        _layoutKnown = true;
        if (!changed) return; // WM_SETTINGCHANGE fires for plenty of unrelated settings

        _edge = edge;
        _band = band;
        _zone = CoverGeometry.RevealZone(band, edge, CoverGeometry.RevealZoneDepth);

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            band.left, band.top, band.right - band.left, band.bottom - band.top,
            NativeMethods.SWP_NOACTIVATE);

        DeckleShellTaskbarCoverSource.Log.LayoutRebuilt();
        DeckleShellTaskbarCoverSource.Log.LayoutRebuiltDetail(
            edge.ToString(), band.left, band.top, band.right, band.bottom, reason);

        // The zone moved with the band — recompute where the cursor stands.
        ResetZoneStateFromCursor();
        UpdateCover("layout_rebuilt");
    }

    // Direct re-read of the cursor against the current zone, cancelling any
    // pending re-cover delay: used at boot, on layout change and on resume,
    // where the transition logic (and its 5 s hysteresis) would be stale.
    private void ResetZoneStateFromCursor()
    {
        if (_recoverTimerArmed)
        {
            KillTimer(_hwnd, TIMER_RECOVER);
            _recoverTimerArmed = false;
        }
        _cursorInRevealZone = !_layoutKnown
            || !NativeMethods.GetCursorPos(out var p)
            || CoverGeometry.Contains(_zone, p);
    }

    // ── Gates ─────────────────────────────────────────────────────────────

    private void EnterSuspendedState(string reason)
    {
        if (_systemSuspended) return;
        _systemSuspended = true;

        if (_recoverTimerArmed)
        {
            KillTimer(_hwnd, TIMER_RECOVER);
            _recoverTimerArmed = false;
        }

        UpdateCover("suspended");
        DeckleShellTaskbarCoverSource.Log.SystemSuspended();
        DeckleShellTaskbarCoverSource.Log.SystemSuspendedDetail(reason);
    }

    private void ExitSuspendedState(string reason)
    {
        if (!_systemSuspended) return;
        _systemSuspended = false;

        ResetZoneStateFromCursor();
        _foregroundHwnd = NativeMethods.GetForegroundWindow();
        EvaluateAppSuppressed();

        UpdateCover("resumed");
        DeckleShellTaskbarCoverSource.Log.SystemResumed();
        DeckleShellTaskbarCoverSource.Log.SystemResumedDetail(reason);
    }

    // Two stages: the shell's own notion of "busy fullscreen" first (cheap,
    // catches D3D exclusive and presentation mode), then the geometric test —
    // the foreground window's DWM visual bounds covering its whole monitor
    // (catches borderless fullscreen and F11).
    private void EvaluateAppSuppressed()
    {
        bool suppressed = false;
        string stage = "none";

        if (SHQueryUserNotificationState(out int quns) == 0
            && quns is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
        {
            suppressed = true;
            stage = "notification_state";
        }

        if (!suppressed)
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && !IsDesktopWindow(fg)
                && DwmGetWindowAttribute(fg, DWMWA_EXTENDED_FRAME_BOUNDS,
                       out var bounds, (uint)Marshal.SizeOf<NativeMethods.RECT>()) == 0)
            {
                IntPtr mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
                {
                    var m = mi.rcMonitor;
                    suppressed = bounds.left <= m.left && bounds.top <= m.top
                              && bounds.right >= m.right && bounds.bottom >= m.bottom;
                    if (suppressed) stage = "fullscreen_geometry";
                }
            }
        }

        if (suppressed == _appSuppressed) return;
        _appSuppressed = suppressed;
        if (suppressed)
        {
            DeckleShellTaskbarCoverSource.Log.CoverSuppressed();
            DeckleShellTaskbarCoverSource.Log.CoverSuppressedDetail(stage);
        }
        else
        {
            DeckleShellTaskbarCoverSource.Log.CoverUnsuppressed();
        }
        UpdateCover(suppressed ? "fullscreen_enter" : "fullscreen_exit");
    }

    // The desktop covers the whole monitor and would pass the fullscreen
    // geometry test: clicking the wallpaper makes it foreground and the band
    // would stand down over a bare desktop until something else takes the
    // foreground. Excluded by the canonical guard Windows itself uses (the
    // shell and root desktop windows) plus the WorkerW host that backs an
    // animated or secondary wallpaper.
    private static bool IsDesktopWindow(IntPtr hwnd)
    {
        if (hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return true;

        var cls = new System.Text.StringBuilder(16);
        return NativeMethods.GetClassName(hwnd, cls, cls.Capacity) > 0
            && cls.ToString() == "WorkerW";
    }

}

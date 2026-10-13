using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.Foreground;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── Foreground ────────────────────────────────────────────────────────

    // Raised by the watcher on its own thread (the UI thread): hand the news
    // to the cover thread, which owns every state field. Only the band handle
    // is read here, published before _running turned true; a post racing a
    // Stop lands on a destroyed window and fails harmlessly.
    private void OnForegroundChanged(ForegroundState state)
    {
        if (_running) PostMessage(_hwnd, WM_APP_FOREGROUND, IntPtr.Zero, IntPtr.Zero);
    }

    // The foreground switched or toggled fullscreen in place: reconcile
    // suppression, and — while the band is up — climb back above the taskbar,
    // which Explorer re-asserts topmost on fullscreen exit.
    private void ApplyForeground(ForegroundState state)
    {
        if (_systemSuspended || !_layoutKnown) return;

        UpdateAppSuppressed(state);
        if (_coverVisible) ReassertTopmost();
    }

    // ── Geometry ──────────────────────────────────────────────────────────

    private void RebuildLayout(string reason)
    {
        var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) == IntPtr.Zero)
        {
            // No fabricated fallback: a wrong band is worse than no band.
            // The cover stays hidden until a later TaskbarCreated,
            // WM_SETTINGCHANGE or WM_DISPLAYCHANGE finds the taskbar.
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

        // Re-read even when the rect is unchanged: a WM_DISPLAYCHANGE can
        // invalidate monitor handles under the same taskbar rect. The verdict
        // is re-applied from the watcher's snapshot, so a band that moved to
        // another monitor — or a layout that was unknown while the foreground
        // changed — re-evaluates suppression now rather than at the next
        // foreground change.
        _bandMonitor = MonitorFromRect(ref band, MONITOR_DEFAULTTONEAREST);

        if (!changed)
        {
            UpdateAppSuppressed(_foreground.Current);
            // Same rect, but Explorer may have re-created or restacked the
            // taskbar. It is topmost too and the last positioned topmost
            // window wins, so without this the band would stay under it until
            // the next foreground change. WM_SETTINGCHANGE also fires for many
            // unrelated settings, which is why nothing else is redone.
            if (_coverVisible) ReassertTopmost();
            return;
        }

        _edge = edge;
        _band = band;
        _zone = CoverGeometry.ApproachZone(band, edge);

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            band.left, band.top, band.right - band.left, band.bottom - band.top,
            NativeMethods.SWP_NOACTIVATE);

        DeckleShellTaskbarCoverSource.Log.LayoutRebuilt();
        DeckleShellTaskbarCoverSource.Log.LayoutRebuiltDetail(
            edge.ToString(), band.left, band.top, band.right, band.bottom, reason);

        // Suppression after the band is in place, so a show it triggers never
        // lands on the old rect or a stale alpha.
        ResetZoneStateFromCursor();
        UpdateAppSuppressed(_foreground.Current);
        UpdateCover("layout_rebuilt");
    }

    // Boot, layout change and resume re-read the alpha from the cursor, since
    // the last one may be stale. Unknown layout or cursor falls back to 0, the
    // pessimistic "revealed" state. The caller runs UpdateCover.
    private void ResetZoneStateFromCursor()
    {
        byte alpha = _layoutKnown && NativeMethods.GetCursorPos(out var p)
            ? CoverAlphaAt(p)
            : (byte)0;
        if (alpha != _coverAlpha) ApplyCoverAlpha(alpha);
    }

    // ── Gates ─────────────────────────────────────────────────────────────

    private void EnterSuspendedState(string reason)
    {
        if (_systemSuspended) return;
        _systemSuspended = true;

        UpdateCover("suspended");
        DeckleShellTaskbarCoverSource.Log.SystemSuspended();
        DeckleShellTaskbarCoverSource.Log.SystemSuspendedDetail(reason);
    }

    private void ExitSuspendedState(string reason)
    {
        if (!_systemSuspended) return;
        _systemSuspended = false;

        ResetZoneStateFromCursor();
        UpdateAppSuppressed(_foreground.Current);

        UpdateCover("resumed");
        DeckleShellTaskbarCoverSource.Log.SystemResumed();
        DeckleShellTaskbarCoverSource.Log.SystemResumedDetail(reason);
    }

    private void UpdateAppSuppressed(ForegroundState state)
    {
        bool suppressed = CoverSuppression.Applies(state, _bandMonitor);
        if (suppressed == _appSuppressed) return;
        _appSuppressed = suppressed;
        if (suppressed)
        {
            DeckleShellTaskbarCoverSource.Log.CoverSuppressed();
            DeckleShellTaskbarCoverSource.Log.CoverSuppressedDetail(
                state.Fullscreen == FullscreenStage.NotificationState
                    ? "notification_state" : "fullscreen_geometry");
        }
        else
        {
            DeckleShellTaskbarCoverSource.Log.CoverUnsuppressed();
        }
        UpdateCover(suppressed ? "fullscreen_enter" : "fullscreen_exit");
    }

}

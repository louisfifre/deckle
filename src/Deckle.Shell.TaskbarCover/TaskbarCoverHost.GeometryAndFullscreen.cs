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
        if (!changed)
        {
            // Same rect, but the taskbar may have been re-created (Explorer
            // restart, a Windhawk mod re-injecting: TaskbarCreated) or
            // restacked by Explorer during the setting or display change. It
            // is topmost too and the last positioned topmost window wins, so
            // without this the band would stay under it, still "visible",
            // until the next foreground change. One SetWindowPos on a
            // user-cadence message; WM_SETTINGCHANGE also fires for plenty
            // of unrelated settings, which is why nothing else is redone.
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

        // The zone moved with the band — recompute where the cursor stands.
        ResetZoneStateFromCursor();
        UpdateCover("layout_rebuilt");
    }

    // Direct re-read of the band's alpha from where the cursor stands now:
    // used at boot, on layout change and on resume, where the last alpha may
    // be stale. Unknown layout or cursor falls back to 0, the pessimistic
    // "revealed" state. The caller runs UpdateCover with its own reason.
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

    // A fullscreen foreground — as the watcher's two-stage test reads it —
    // stands the band down. Only a change of verdict logs and updates.
    private void UpdateAppSuppressed(ForegroundState state)
    {
        bool suppressed = state.IsFullscreen;
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

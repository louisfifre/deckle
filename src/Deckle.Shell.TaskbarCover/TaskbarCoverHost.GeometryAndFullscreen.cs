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
    // to the cover thread, which owns every state field. Only the anchor
    // handle is read here, published before _running turned true; a post
    // racing a Stop lands on a destroyed window and fails harmlessly.
    private void OnForegroundChanged(ForegroundState state)
    {
        if (_running) PostMessage(_hwnd, WM_APP_FOREGROUND, IntPtr.Zero, IntPtr.Zero);
    }

    // The foreground switched or toggled fullscreen in place: reconcile each
    // band's suppression, and — while a band is up — climb back above its
    // taskbar, which Explorer re-asserts topmost on fullscreen exit.
    private void ApplyForeground(ForegroundState state)
    {
        if (_systemSuspended || !_layoutKnown) return;

        foreach (var band in _bands)
        {
            UpdateAppSuppressed(band, state);
            if (band.Visible) band.ReassertTopmost();
        }
    }

    // ── Geometry ──────────────────────────────────────────────────────────

    private void RebuildLayout(string reason)
    {
        var placements = TaskbarLocator.Locate();
        if (placements.Count == 0)
        {
            // No fabricated fallback: a wrong band is worse than no band.
            // The bands stay hidden until a later TaskbarCreated,
            // WM_SETTINGCHANGE or WM_DISPLAYCHANGE finds the taskbar.
            if (!_layoutFailureLogged)
            {
                DeckleShellTaskbarCoverSource.Log.LayoutQueryFailed();
                _layoutFailureLogged = true;
            }
            _layoutKnown = false;
            foreach (var band in _bands) UpdateCover(band, "layout_unknown");
            return;
        }
        if (_layoutFailureLogged)
            DeckleShellTaskbarCoverSource.Log.LayoutQueryRecovered();
        _layoutFailureLogged = false;

        // One band per taskbar: a monitor that left takes its band with it, a
        // monitor that arrived gets a fresh one. A band that cannot be
        // created leaves its taskbar uncovered and the others unaffected.
        while (_bands.Count > placements.Count) DestroyBand(_bands[^1]);
        while (_bands.Count < placements.Count)
        {
            if (!TryCreateCoverWindow(out var hwnd))
            {
                DeckleShellTaskbarCoverSource.Log.BandCreateFailed();
                DeckleShellTaskbarCoverSource.Log.BandCreateFailedDetail(
                    _bands.Count, Marshal.GetLastWin32Error());
                break;
            }
            _bands.Add(new CoverBand(_bands.Count, hwnd));
        }

        var approach = _requestedApproach;
        _layoutKnown = true;
        _approach = approach;

        bool anyPlaced = false;
        for (int i = 0; i < _bands.Count; i++)
        {
            var band = _bands[i];
            var placement = placements[i];

            // Re-read even when the rect is unchanged: a WM_DISPLAYCHANGE can
            // invalidate monitor handles under the same taskbar rect. The
            // verdict is re-applied from the watcher's snapshot, so a band
            // that moved to another monitor — or a layout that was unknown
            // while the foreground changed — re-evaluates suppression now
            // rather than at the next foreground change.
            if (!band.PlacementDiffers(placement, approach))
            {
                UpdateAppSuppressed(band, _foreground.Current);
                // Same rect, but Explorer may have re-created or restacked the
                // taskbar. It is topmost too and the last positioned topmost
                // window wins, so without this the band would stay under it
                // until the next foreground change. WM_SETTINGCHANGE also
                // fires for many unrelated settings, which is why nothing
                // else is redone.
                if (band.Visible) band.ReassertTopmost();
                continue;
            }

            band.Place(placement, approach);
            anyPlaced = true;
            DeckleShellTaskbarCoverSource.Log.LayoutRebuiltDetail(
                band.Index, placements.Count, band.Edge.ToString(),
                band.Rect.left, band.Rect.top, band.Rect.right, band.Rect.bottom,
                (int)Math.Round(band.DpiScale * 100), reason);

            // Suppression after the band is in place, so a show it triggers
            // never lands on the old rect or a stale alpha.
            ResetZoneStateFromCursor(band);
            UpdateAppSuppressed(band, _foreground.Current);
            UpdateCover(band, "layout_rebuilt");
        }

        if (anyPlaced) DeckleShellTaskbarCoverSource.Log.LayoutRebuilt();
    }

    // The band's taskbar is gone with its monitor: hide it with its own
    // transition, then drop the window.
    private void DestroyBand(CoverBand band)
    {
        if (band.Visible) band.Hide("taskbar_gone");
        NativeMethods.DestroyWindow(band.Hwnd);
        _bands.Remove(band);
    }

    // Boot, layout change and resume re-read a band's alpha from the cursor,
    // since the last one may be stale. Unknown layout or cursor falls back to
    // 0, the pessimistic "revealed" state. The caller runs UpdateCover.
    private void ResetZoneStateFromCursor(CoverBand band)
    {
        byte alpha = _layoutKnown && NativeMethods.GetCursorPos(out var p)
            ? band.AlphaAt(p)
            : (byte)0;
        if (alpha != band.Alpha) band.ApplyAlpha(alpha);
    }

    // ── Gates ─────────────────────────────────────────────────────────────

    private void EnterSuspendedState(string reason)
    {
        if (_systemSuspended) return;
        _systemSuspended = true;

        foreach (var band in _bands) UpdateCover(band, "suspended");
        DeckleShellTaskbarCoverSource.Log.SystemSuspended();
        DeckleShellTaskbarCoverSource.Log.SystemSuspendedDetail(reason);
    }

    private void ExitSuspendedState(string reason)
    {
        if (!_systemSuspended) return;
        _systemSuspended = false;

        foreach (var band in _bands)
        {
            ResetZoneStateFromCursor(band);
            UpdateAppSuppressed(band, _foreground.Current);
            UpdateCover(band, "resumed");
        }
        DeckleShellTaskbarCoverSource.Log.SystemResumed();
        DeckleShellTaskbarCoverSource.Log.SystemResumedDetail(reason);
    }

    private void UpdateAppSuppressed(CoverBand band, ForegroundState state)
    {
        bool suppressed = CoverSuppression.Applies(state, band.Monitor);
        if (suppressed == band.AppSuppressed) return;
        band.AppSuppressed = suppressed;
        if (suppressed)
        {
            DeckleShellTaskbarCoverSource.Log.CoverSuppressed();
            DeckleShellTaskbarCoverSource.Log.CoverSuppressedDetail(
                band.Index,
                state.Fullscreen == FullscreenStage.NotificationState
                    ? "notification_state" : "fullscreen_geometry");
        }
        else
        {
            DeckleShellTaskbarCoverSource.Log.CoverUnsuppressed();
            DeckleShellTaskbarCoverSource.Log.CoverUnsuppressedDetail(band.Index);
        }
        UpdateCover(band, suppressed ? "fullscreen_enter" : "fullscreen_exit");
    }
}

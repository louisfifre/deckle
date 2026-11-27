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
    // band's suppression.
    private void ApplyForeground(ForegroundState state)
    {
        if (_systemSuspended || !_layoutKnown) return;

        foreach (var band in _bands) UpdateAppSuppressed(band, state);
        RefreshUnderlays("foreground");
    }

    // ── Geometry ──────────────────────────────────────────────────────────

    private void RebuildLayout(string reason)
    {
        var placements = TaskbarLocator.Locate();
        if (placements.Count == 0)
        {
            // No fabricated fallback: a wrong band is worse than no band.
            // The taskbars are given back whole until a later TaskbarCreated,
            // WM_SETTINGCHANGE or WM_DISPLAYCHANGE finds the taskbar.
            if (!_layoutFailureLogged)
            {
                DeckleShellTaskbarCoverSource.Log.LayoutQueryFailed();
                _layoutFailureLogged = true;
            }
            _layoutKnown = false;
            foreach (var band in _bands) UpdateCover(band, "layout_unknown");
            RefreshUnderlays("layout_unknown");
            return;
        }
        if (_layoutFailureLogged)
            DeckleShellTaskbarCoverSource.Log.LayoutQueryRecovered();
        _layoutFailureLogged = false;

        // One band per taskbar: a monitor that left takes its band with it, a
        // monitor that arrived gets a fresh one.
        while (_bands.Count > placements.Count) DropBand(_bands[^1]);
        while (_bands.Count < placements.Count) _bands.Add(new CoverBand(_bands.Count));

        int approachDip = _requestedApproachDip;
        _layoutKnown = true;
        _approachDip = approachDip;

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
            if (!band.PlacementDiffers(placement, approachDip))
            {
                UpdateAppSuppressed(band, _foreground.Current);
                continue;
            }

            // The band moves to another window (Explorer re-created the
            // taskbar, or the secondaries changed order): the one it leaves is
            // given back whole first, harmless when it is already gone.
            if (band.Taskbar != placement.Taskbar)
                ApplyTaskbarAlpha(band, TaskbarFade.Opaque, "taskbar_changed");
            band.Place(placement, approachDip);
            anyPlaced = true;
            DeckleShellTaskbarCoverSource.Log.LayoutRebuiltDetail(
                band.Index, placements.Count, band.Edge.ToString(),
                band.Rect.left, band.Rect.top, band.Rect.right, band.Rect.bottom,
                (int)Math.Round(band.DpiScale * 100), reason);

            // Suppression after the band is in place, so the alpha it sets
            // never comes from the old rect or a stale level.
            ResetZoneStateFromCursor(band);
            UpdateAppSuppressed(band, _foreground.Current);
            UpdateCover(band, "layout_rebuilt");
        }

        if (anyPlaced) DeckleShellTaskbarCoverSource.Log.LayoutRebuilt();
        RefreshUnderlays("layout_rebuilt");
    }

    // The band's taskbar is gone with its monitor, or hidden: it is given
    // back whole in case it shows again, then the band goes.
    private void DropBand(CoverBand band)
    {
        ApplyTaskbarAlpha(band, TaskbarFade.Opaque, "taskbar_gone");
        DisposeUnderlay(band);
        _bands.Remove(band);
        PublishFaded();
    }

    // Boot, layout change and resume re-read a band's level from the cursor,
    // since the last one may be stale. Unknown layout or cursor falls back to
    // 0, the pessimistic "whole" state. The caller runs UpdateCover.
    private void ResetZoneStateFromCursor(CoverBand band)
    {
        byte level = _layoutKnown && NativeMethods.GetCursorPos(out var p)
            ? band.LevelAt(p)
            : (byte)0;
        band.SetLevel(level);
    }

    // ── Gates ─────────────────────────────────────────────────────────────

    private void EnterSuspendedState(string reason)
    {
        if (_systemSuspended) return;
        _systemSuspended = true;

        foreach (var band in _bands) UpdateCover(band, "suspended");
        RefreshUnderlays("suspended");
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
        RefreshUnderlays("resumed");
        DeckleShellTaskbarCoverSource.Log.SystemResumed();
        DeckleShellTaskbarCoverSource.Log.SystemResumedDetail(reason);
    }

    private void UpdateAppSuppressed(CoverBand band, ForegroundState state)
    {
        // The taskbar taking focus (Windows + T) is read on the same triggers as
        // a fullscreen window. It changes nothing else, so it needs no event of
        // its own: the band's Hidden and Shown lines carry the reason.
        bool focused = CoverSuppression.TaskbarFocused(state, band.Taskbar);
        if (focused != band.TaskbarFocused)
        {
            band.TaskbarFocused = focused;
            UpdateCover(band, focused ? "taskbar_focused" : "taskbar_unfocused");
        }

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

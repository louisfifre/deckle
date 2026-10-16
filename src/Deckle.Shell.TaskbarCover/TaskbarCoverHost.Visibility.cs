using System.Runtime.InteropServices;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Shell.TaskbarCover;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── Visibility — the sole ShowWindow site, idempotent ─────────────────

    private void UpdateCover(string reason)
    {
        bool shouldBeVisible = _layoutKnown && !_appSuppressed
                            && !_systemSuspended && _coverAlpha > 0;
        if (shouldBeVisible == _coverVisible) return;

        _coverVisible = shouldBeVisible;
        if (shouldBeVisible)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
            ReassertTopmost();
            // Z-order witness: ShowWindow and the topmost assert can both
            // succeed while the band still sits below Shell_TrayWnd. CoverShown
            // only proves the ShowWindow call; this records what occludes the
            // band right after the assert, and the foreground at that instant.
            WindowingProbe.EmitWindowZOrderState(_hwnd, "taskbar-cover", "after_show_topmost");
            DeckleShellTaskbarCoverSource.Log.CoverShown(reason);
        }
        else
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
            DeckleShellTaskbarCoverSource.Log.CoverHidden(reason);
        }
    }

    // The taskbar is topmost too and among topmost windows the most recently
    // positioned wins, so the band re-asserts its place on show and, while
    // visible, on every foreground change, every layout message (even with an
    // unchanged rect), every return to full opacity, and whenever the desktop
    // reorders the taskbar above it.
    private void ReassertTopmost() =>
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    // The process's global EVENT_OBJECT_REORDER hook. Explorer can restack
    // the taskbar above the band with no foreground change, no layout message
    // and no cursor leaving the zone — the one case the other reconciliation
    // points miss, and the reason the band seemed to vanish. The desktop
    // raises REORDER each time the top-level stack moves, our own re-assert
    // included, so the band climbs back only when the taskbar is actually
    // above it: the re-assert then puts it on top, and the REORDER it causes
    // finds nothing to do. No timer, no poll, bounded by Explorer's own
    // restacks.
    private void OnDesktopReorder(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // Hidden covers the suspended and layout-unknown states too; a show
        // re-asserts on its own.
        if (!_coverVisible) return;
        if (hwnd != GetDesktopWindow()) return;

        if (!CoverStacking.TaskbarAbove(_hwnd, _taskbar, static h => NativeMethods.GetWindow(h, GW_HWNDPREV)))
            return;

        ReassertTopmost();
        // Same witness as the show path: whether the assert actually put the
        // band back above Shell_TrayWnd, or silently left it under.
        WindowingProbe.EmitWindowZOrderState(_hwnd, "taskbar-cover", "after_desktop_reorder");
        DeckleShellTaskbarCoverSource.Log.CoverRestacked("desktop_reorder");
    }
}

using Deckle.Core;
using Deckle.Diagnostics;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── Visibility — the one decision site, idempotent ────────────────────
    //
    // A band shows when the layout is known, nothing fullscreen sits on its
    // monitor, the machine is awake and unlocked, and the cursor leaves it
    // some opacity. The band itself performs the show or hide.

    private void UpdateCover(CoverBand band, string reason)
    {
        bool shouldBeVisible = _layoutKnown && !band.AppSuppressed
                            && !_systemSuspended && band.Alpha > 0;
        if (shouldBeVisible == band.Visible) return;

        if (shouldBeVisible) band.Show(reason);
        else                 band.Hide(reason);
    }

    // The process's global EVENT_OBJECT_REORDER hook. Explorer can restack a
    // taskbar above its band with no foreground change, no layout message and
    // no cursor leaving the zone — the one case the other reconciliation
    // points miss, and the reason a band seemed to vanish. The desktop raises
    // REORDER each time the top-level stack moves, our own re-asserts
    // included, so a band climbs back only when its taskbar is actually above
    // it: the re-assert then puts it on top, and the REORDER it causes finds
    // nothing to do. No timer, no poll, bounded by Explorer's own restacks.
    private void OnDesktopReorder(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (hwnd != GetDesktopWindow()) return;

        foreach (var band in _bands)
        {
            // Hidden covers the suspended and layout-unknown states too; a
            // show re-asserts on its own.
            if (!band.Visible) continue;
            if (!CoverStacking.TaskbarAbove(band.Hwnd, band.Taskbar,
                    static h => NativeMethods.GetWindow(h, GW_HWNDPREV)))
                continue;

            band.ReassertTopmost();
            // Same witness as the show path: whether the assert actually put
            // the band back above its taskbar, or silently left it under.
            WindowingProbe.EmitWindowZOrderState(band.Hwnd, "taskbar-cover", "after_desktop_reorder");
            DeckleShellTaskbarCoverSource.Log.CoverRestacked(band.Index, "desktop_reorder");
        }
    }
}

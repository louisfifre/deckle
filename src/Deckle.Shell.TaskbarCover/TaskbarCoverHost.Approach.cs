using Deckle.Core;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    /// <summary>
    /// How far from a taskbar the pointer starts to reveal it. Safe from any
    /// thread: a change on a running host posts WM_APP_APPROACH and the cover
    /// thread rebuilds its zones; a stopped host builds them at this level on
    /// its next Start.
    /// </summary>
    public ApproachDistance Approach
    {
        get => _requestedApproach;
        set
        {
            if (_requestedApproach == value) return;
            _requestedApproach = value;
            // Same hand-off as OnForegroundChanged: only the anchor handle is
            // read here, and a post racing a Stop fails harmlessly.
            if (_running) PostMessage(_hwnd, WM_APP_APPROACH, IntPtr.Zero, IntPtr.Zero);
        }
    }

    // The process's single global EVENT_OBJECT_LOCATIONCHANGE hook. It runs at
    // input cadence, so it stays trivial: outside every zone with the bands
    // opaque it costs one GetCursorPos and, per band, one rect test and one
    // byte compare. The zones never overlap — each is bounded to its band's
    // extent on its own monitor — so at most one band fades at a time.
    // The foreground window's in-place F11 toggle reaches the host through
    // the ForegroundWatcher, whose own location hook is narrowed to the
    // foreground process.
    private void OnLocationChange(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (_systemSuspended || !_layoutKnown) return;

        if (idObject != OBJID_CURSOR || hwnd != IntPtr.Zero) return;
        if (!NativeMethods.GetCursorPos(out var p)) return;

        foreach (var band in _bands)
        {
            if (!CoverGeometry.Contains(band.Zone, p))
            {
                if (band.Alpha != 255) SetCoverAlpha(band, 255, "zone_exit");
                continue;
            }

            SetCoverAlpha(band, band.AlphaAt(p), "zone_fade");
        }
    }

    // The one entry point that moves a band's opacity. Applies the alpha
    // before UpdateCover may show the window, so it never flashes at a stale
    // value. Back to fully opaque, the band re-asserts its place above the
    // taskbar: the zone exit is a natural reconciliation point, and the
    // taskbar may have restacked itself while the cursor was over it.
    private void SetCoverAlpha(CoverBand band, byte alpha, string reason)
    {
        if (alpha == band.Alpha) return;
        bool wasVisible = band.Visible;

        band.ApplyAlpha(alpha);
        UpdateCover(band, reason);

        // A fresh show already re-asserted topmost inside Show.
        if (alpha == 255 && wasVisible && band.Visible) band.ReassertTopmost();
    }
}

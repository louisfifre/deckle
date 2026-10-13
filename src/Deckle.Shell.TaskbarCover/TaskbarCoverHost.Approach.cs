using Deckle.Core;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // The process's single global EVENT_OBJECT_LOCATIONCHANGE hook. It runs at
    // input cadence, so it stays trivial: outside the zone with the band
    // opaque it costs one GetCursorPos, one rect test and one byte compare.
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

        if (!CoverGeometry.Contains(_zone, p))
        {
            if (_coverAlpha == 255) return;
            SetCoverAlpha(255, "zone_exit");
            return;
        }

        SetCoverAlpha(CoverAlphaAt(p), "zone_fade");
    }

    private byte CoverAlphaAt(POINT p) =>
        (byte)Math.Round(CoverGeometry.Opacity(_band, _edge, p) * 255);

    // The one entry point that moves the band's opacity. Applies the alpha
    // before UpdateCover may show the window, so it never flashes at a stale
    // value. Back to fully opaque, the band re-asserts its place above the
    // taskbar: the zone exit is a natural reconciliation point, and the
    // taskbar may have restacked itself while the cursor was over it.
    private void SetCoverAlpha(byte alpha, string reason)
    {
        if (alpha == _coverAlpha) return;
        bool wasVisible = _coverVisible;

        ApplyCoverAlpha(alpha);
        UpdateCover(reason);

        // A fresh show already re-asserted topmost inside UpdateCover.
        if (alpha == 255 && wasVisible && _coverVisible) ReassertTopmost();
    }

    // At 0 the window is hidden by UpdateCover instead, so no click lands on
    // an invisible band.
    private void ApplyCoverAlpha(byte alpha)
    {
        _coverAlpha = alpha;
        if (alpha > 0)
            NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }
}

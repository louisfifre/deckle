using Deckle.Core;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

public sealed partial class TaskbarCoverHost
{
    // ── Cursor signal ─────────────────────────────────────────────────────

    // The single EVENT_OBJECT_LOCATIONCHANGE hook carries two signals across
    // every process: the cursor (hwnd == NULL + OBJID_CURSOR) drives the
    // band's opacity in the approach zone; a geometry change on the
    // *foreground* window (OBJID_WINDOW, hwnd == _foregroundHwnd) is the
    // in-place F11 toggle, which raises no foreground event. Runs at input
    // cadence: outside the zone with the band already opaque, the cursor path
    // costs one GetCursorPos, one rect test and one byte compare.
    private void OnLocationChange(
        IntPtr hWinEventHook, uint evt, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        if (_systemSuspended || !_layoutKnown) return;

        // The foreground window resized in place (F11): re-evaluate
        // suppression and, while visible, climb back over the taskbar.
        if (idObject == OBJID_WINDOW && idChild == 0
            && hwnd != IntPtr.Zero && hwnd == _foregroundHwnd)
        {
            EvaluateAppSuppressed();
            if (_coverVisible) ReassertTopmost();
            return;
        }

        // Cursor moves: the approach-zone fade.
        if (idObject != OBJID_CURSOR || hwnd != IntPtr.Zero) return;
        if (!NativeMethods.GetCursorPos(out var p)) return;

        if (!CoverGeometry.Contains(_zone, p))
        {
            // Out of the zone: the band re-covers at once, fully opaque.
            if (_coverAlpha == 255) return;
            SetCoverAlpha(255, "zone_exit");
            return;
        }

        SetCoverAlpha(CoverAlphaAt(p), "zone_fade");
    }

    // Alpha byte of the band for a cursor at p.
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

    // Stores the alpha and hands it to the layered window. At 0 the window is
    // hidden by UpdateCover instead, so no click lands on an invisible band.
    private void ApplyCoverAlpha(byte alpha)
    {
        _coverAlpha = alpha;
        if (alpha > 0)
            NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }
}

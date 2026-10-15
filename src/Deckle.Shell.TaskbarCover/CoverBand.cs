using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Shell.TaskbarCover;

// One band: the opaque black topmost window laid over one taskbar, and the
// state that belongs to that taskbar alone — its rect and edge, its monitor
// and DPI, its approach zone, its opacity, its visibility and its fullscreen
// suppression. The host owns the thread, the hooks and the gates every band
// shares (sleep, lock, layout known) and drives each band through the
// methods here. Worker thread only, like the host's own state.
internal sealed class CoverBand
{
    // Ordinal in the host's list, the identifier the log carries: 0 is the
    // primary taskbar's band, the others follow the desktop top to bottom,
    // left to right.
    public int Index { get; }
    public IntPtr Hwnd { get; }

    public CoverBand(int index, IntPtr hwnd)
    {
        Index = index;
        Hwnd = hwnd;
    }

    // ── Placement ─────────────────────────────────────────────────────────

    // False until the first layout lands on the band; a fresh band always
    // differs from any placement, so its first Place is never skipped.
    public bool Placed { get; private set; }
    public IntPtr Taskbar { get; private set; }
    public TaskbarEdge Edge { get; private set; }
    public NativeMethods.RECT Rect { get; private set; }
    public NativeMethods.RECT Zone { get; private set; }
    // Fullscreen suppression only applies to a window on this monitor.
    public IntPtr Monitor { get; private set; }
    public double DpiScale { get; private set; } = 1.0;
    // Level the zone was built at.
    public ApproachDistance Approach { get; private set; }

    // The approach level and the monitor's DPI are part of the placement: a
    // new level, or a scale change under the same rect, rebuilds the zone
    // and re-reads the alpha like a moved taskbar does.
    public bool PlacementDiffers(TaskbarPlacement p, ApproachDistance approach)
        => !Placed
        || p.Edge != Edge || approach != Approach || p.DpiScale != DpiScale
        || p.Rect.left != Rect.left || p.Rect.top != Rect.top
        || p.Rect.right != Rect.right || p.Rect.bottom != Rect.bottom;

    public void Place(TaskbarPlacement p, ApproachDistance approach)
    {
        Placed   = true;
        Taskbar  = p.Taskbar;
        Edge     = p.Edge;
        Rect     = p.Rect;
        Monitor  = p.Monitor;
        DpiScale = p.DpiScale;
        Approach = approach;
        Zone     = CoverGeometry.ApproachZone(Rect, Edge, approach, DpiScale);

        NativeMethods.SetWindowPos(Hwnd, NativeMethods.HWND_TOPMOST,
            Rect.left, Rect.top, Rect.right - Rect.left, Rect.bottom - Rect.top,
            NativeMethods.SWP_NOACTIVATE);
    }

    // ── Opacity ───────────────────────────────────────────────────────────

    // 0 = taskbar revealed, the pessimistic boot value, corrected from the
    // real cursor once the band is placed; 255 = fully covered.
    public byte Alpha { get; private set; }

    public byte AlphaAt(POINT p) =>
        (byte)Math.Round(CoverGeometry.Opacity(Rect, Edge, p, Approach, DpiScale) * 255);

    // At 0 the window is hidden by the host instead, so no click lands on an
    // invisible band.
    public void ApplyAlpha(byte alpha)
    {
        Alpha = alpha;
        if (alpha > 0)
            NativeMethods.SetLayeredWindowAttributes(Hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
    }

    // ── Visibility — the band's only ShowWindow sites ─────────────────────

    public bool Visible { get; private set; }
    // A fullscreen/presentation app is foreground on this band's monitor.
    public bool AppSuppressed { get; set; }

    public void Show(string reason)
    {
        Visible = true;
        NativeMethods.ShowWindow(Hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        ReassertTopmost();
        // Z-order witness: ShowWindow and the topmost assert can both succeed
        // while the band still sits below its taskbar window. CoverShown only
        // proves the ShowWindow call; this records what occludes the band
        // right after the assert, and the foreground at that instant.
        WindowingProbe.EmitWindowZOrderState(Hwnd, "taskbar-cover", "after_show_topmost");
        DeckleShellTaskbarCoverSource.Log.CoverShown(Index, reason);
    }

    public void Hide(string reason)
    {
        Visible = false;
        NativeMethods.ShowWindow(Hwnd, NativeMethods.SW_HIDE);
        DeckleShellTaskbarCoverSource.Log.CoverHidden(Index, reason);
    }

    // The taskbar is topmost too and among topmost windows the most recently
    // positioned wins, so the band re-asserts its place on show and, while
    // visible, on every foreground change, every layout message (even with an
    // unchanged rect) and every return to full opacity.
    public void ReassertTopmost() =>
        NativeMethods.SetWindowPos(Hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
}

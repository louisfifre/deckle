using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// One band: the state that belongs to one taskbar alone — its window, rect
// and edge, its monitor and DPI, its approach zone, its cover level, the
// alpha its taskbar was last given and its fullscreen suppression. The band
// draws nothing: it fades its taskbar (TaskbarFade). The host owns the
// thread, the hooks and the gates every band shares (sleep, lock, layout
// known) and drives each band through the members here. Worker thread only,
// like the host's own state.
internal sealed class CoverBand
{
    // Ordinal in the host's list, the identifier the log carries: 0 is the
    // primary taskbar's band, the others follow the desktop top to bottom,
    // left to right.
    public int Index { get; }

    public CoverBand(int index)
    {
        Index = index;
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
    public NativeMethods.RECT MonitorRect { get; private set; }
    public double DpiScale { get; private set; } = 1.0;
    // Approach distance, in DIP, the zone was built at.
    public int ApproachDip { get; private set; }

    // The approach distance and the monitor's DPI are part of the placement:
    // a new distance, or a scale change under the same rect, rebuilds the zone
    // and re-reads the level like a moved taskbar does. So is the taskbar
    // window: Explorer re-creates it on a restart, at the same rect, and the
    // new window has never been faded.
    public bool PlacementDiffers(TaskbarPlacement p, int approachDip)
        => !Placed
        || p.Taskbar != Taskbar
        || p.Edge != Edge || approachDip != ApproachDip || p.DpiScale != DpiScale
        || p.Rect.left != Rect.left || p.Rect.top != Rect.top
        || p.Rect.right != Rect.right || p.Rect.bottom != Rect.bottom
        || p.MonitorRect.left != MonitorRect.left || p.MonitorRect.top != MonitorRect.top
        || p.MonitorRect.right != MonitorRect.right || p.MonitorRect.bottom != MonitorRect.bottom;

    public void Place(TaskbarPlacement p, int approachDip)
    {
        Placed   = true;
        Taskbar  = p.Taskbar;
        Edge     = p.Edge;
        Rect     = p.Rect;
        Monitor  = p.Monitor;
        MonitorRect = p.MonitorRect;
        DpiScale = p.DpiScale;
        ApproachDip = approachDip;
        // Converted with this band's own monitor scale: each taskbar may sit
        // on a screen with its own DPI, and capped at half that monitor.
        Zone     = CoverGeometry.ApproachZone(Rect, Edge, approachDip, DpiScale, MonitorRect);
    }

    // ── Level ─────────────────────────────────────────────────────────────

    // How far the cursor asks the taskbar to fade: 0 = taskbar whole, the
    // pessimistic boot value, corrected from the real cursor once the band is
    // placed; 255 = faded to the floor.
    public byte Level { get; private set; }

    public byte LevelAt(POINT p) =>
        (byte)Math.Round(CoverGeometry.Opacity(Rect, Edge, p, ApproachDip, DpiScale, MonitorRect) * 255);

    public void SetLevel(byte level) => Level = level;

    // ── Gates ─────────────────────────────────────────────────────────────

    // A fullscreen/presentation app is foreground on this band's monitor.
    public bool AppSuppressed { get; set; }
    // This band's own taskbar is the foreground window (Windows + T, the
    // Windows key setting): it stays whole until focus moves on.
    public bool TaskbarFocused { get; set; }

    // The strip under the taskbar while an app is maximized on its monitor;
    // created the first time one is (TaskbarCoverHost.Underlay).
    public TaskbarUnderlay? Underlay { get; set; }

    // What reads the edge of the app the underlay continues, the app it
    // reads (zero when none) and the reader's number, which tells its posts
    // from a replaced reader's.
    public Deckle.Vision.WindowEdgeReader? EdgeReader { get; set; }
    public IntPtr EdgeReaderApp { get; set; }
    public int EdgeReaderGeneration { get; set; }

    // ── The taskbar's alpha — the band's only writes to Explorer's window ──

    // The alpha the taskbar was last given; Opaque while the band leaves it
    // as Explorer made it. The host gives a taskbar back whole before the band
    // moves to another window, so the value always describes Taskbar.
    public byte TaskbarAlpha { get; private set; } = TaskbarFade.Opaque;

    public bool Faded => TaskbarAlpha != TaskbarFade.Opaque;

    // Returns 0, or the Win32 error of the call that failed; a failed write
    // leaves TaskbarAlpha as it was, so the next trigger tries again.
    public int ApplyTaskbarAlpha(byte alpha)
    {
        if (alpha == TaskbarAlpha) return 0;

        if (alpha == TaskbarFade.Opaque)
        {
            TaskbarFade.Restore(Taskbar);
            TaskbarAlpha = alpha;
            return 0;
        }

        int error = TaskbarFade.Apply(Taskbar, alpha);
        if (error == 0) TaskbarAlpha = alpha;
        return error;
    }
}

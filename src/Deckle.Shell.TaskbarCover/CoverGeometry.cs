using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// Edge a taskbar is anchored to. Ordinals equal the ABE_* values of the
// appbar API, the historical source of the primary's edge, so a value read
// from an older log keeps its meaning; the edge is now read from geometry
// (EdgeOf) for every taskbar alike.
public enum TaskbarEdge
{
    Left   = 0,
    Top    = 1,
    Right  = 2,
    Bottom = 3,
}

// Pure geometry of the cover band and its approach zone — no Win32 call,
// no state. The band is exactly the taskbar rect; the approach zone is the
// band extended inward from the anchored screen edge by the approach span.
// Inside the zone the band's opacity follows the cursor's distance to the
// band: transparent on the band, opaque again at the zone's outer boundary.
public static class CoverGeometry
{
    // The approach span: how far past the band the zone reaches and the
    // distance the fade runs over. A fixed distance rather than a multiple of
    // the band thickness (Louis, 2026-10-15): a reveal gesture is a mouse
    // movement, and a movement of 64 DIP should feel the same whether the
    // taskbar is 40 or 60 px thick. The setting is in device-independent
    // pixels (ApproachDistance); this is the one conversion to the physical
    // pixels of a monitor at dpiScale (1.0 at 96 DPI, 1.5 at 144), since the
    // cursor and the band rect are physical pixels, so the zone must be too.
    // Each band calls it with its own monitor's scale, so a taskbar on a
    // 200% screen and one on a 100% screen reach the same distance by hand.
    public static int ApproachSpan(int approachDip, double dpiScale)
        => Dpi.ToPixels(approachDip, dpiScale);

    public static int Thickness(NativeMethods.RECT band, TaskbarEdge edge)
        => edge is TaskbarEdge.Left or TaskbarEdge.Right
            ? band.right - band.left
            : band.bottom - band.top;

    // The edge a taskbar hugs, read from its rect against its monitor's: the
    // long side says whether it lies along the top/bottom or the left/right,
    // and the nearer monitor side says which of the two. Nearer rather than
    // equal, so a taskbar slid mostly off-screen by auto-hide, or a rect one
    // pixel off its monitor, still resolves to the edge it belongs to.
    public static TaskbarEdge EdgeOf(NativeMethods.RECT taskbar, NativeMethods.RECT monitor)
    {
        int width  = taskbar.right - taskbar.left;
        int height = taskbar.bottom - taskbar.top;

        if (width >= height)
            return Math.Abs(taskbar.top - monitor.top) <= Math.Abs(monitor.bottom - taskbar.bottom)
                ? TaskbarEdge.Top : TaskbarEdge.Bottom;

        return Math.Abs(taskbar.left - monitor.left) <= Math.Abs(monitor.right - taskbar.right)
            ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    // The zone is the band plus the span, measured inward from the anchored
    // screen edge. It spans only the band's extent along the edge: a cursor
    // near the same screen edge on another monitor stays out of it.
    public static NativeMethods.RECT ApproachZone(
        NativeMethods.RECT band, TaskbarEdge edge, int approachDip, double dpiScale)
    {
        int depth = Thickness(band, edge) + ApproachSpan(approachDip, dpiScale);
        return edge switch
        {
            TaskbarEdge.Left   => new() { left = band.left,           top = band.top,            right = band.left + depth, bottom = band.bottom },
            TaskbarEdge.Right  => new() { left = band.right - depth,  top = band.top,            right = band.right,        bottom = band.bottom },
            TaskbarEdge.Top    => new() { left = band.left,           top = band.top,            right = band.right,        bottom = band.top + depth },
            _                  => new() { left = band.left,           top = band.bottom - depth, right = band.right,        bottom = band.bottom },
        };
    }

    // Perpendicular distance in pixels from the band's inner edge, 0 on or
    // behind the band. The first pixel past the band is at distance 0 on
    // every edge, so the last pixel inside the zone sits one pixel short of
    // the span and the first pixel outside lands exactly on it.
    public static int DistanceToBand(NativeMethods.RECT band, TaskbarEdge edge, POINT p)
    {
        int d = edge switch
        {
            TaskbarEdge.Left   => p.X - band.right,
            TaskbarEdge.Right  => band.left - 1 - p.X,
            TaskbarEdge.Top    => p.Y - band.bottom,
            _                  => band.top - 1 - p.Y,
        };
        return d < 0 ? 0 : d;
    }

    // Smoothstep over the span beyond the band, the same curve as the HUD's
    // proximity fade, so both surfaces ease the same way.
    public static double Opacity(
        NativeMethods.RECT band, TaskbarEdge edge, POINT p, int approachDip, double dpiScale)
    {
        if (!Contains(ApproachZone(band, edge, approachDip, dpiScale), p)) return 1.0;

        int span = ApproachSpan(approachDip, dpiScale);
        if (span <= 0) return 1.0;

        double t = (double)DistanceToBand(band, edge, p) / span;
        if (t < 0.0) t = 0.0;
        if (t > 1.0) t = 1.0;
        return t * t * (3.0 - 2.0 * t);
    }

    // Half-open on right/bottom, the Win32 RECT convention.
    public static bool Contains(NativeMethods.RECT r, POINT p)
        => p.X >= r.left && p.X < r.right && p.Y >= r.top && p.Y < r.bottom;
}

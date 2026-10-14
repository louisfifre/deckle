using Deckle.Core;

namespace Deckle.Shell.TaskbarCover;

// Edge the taskbar is anchored to. Ordinals match the ABE_* values
// returned in APPBARDATA.uEdge by SHAppBarMessage(ABM_GETTASKBARPOS),
// so the native value casts straight into this enum.
public enum TaskbarEdge
{
    Left   = 0,
    Top    = 1,
    Right  = 2,
    Bottom = 3,
}

// Pure geometry of the cover band and its approach zone — no Win32 call,
// no state. The band is exactly the taskbar rect; the approach zone is the
// band extended inward from the anchored screen edge. Inside the zone the
// band's opacity follows the cursor's distance to the band: transparent on
// the band, opaque again at the zone's outer boundary.
public static class CoverGeometry
{
    // Depth of the approach zone at the Standard level, as a multiple of the
    // band thickness, measured inward from the anchored screen edge with the
    // band included. The overloads without a level keep reading it.
    public const int ApproachZoneFactor = 4;

    // The zone depth of each named level, in band thicknesses rather than
    // pixels, so a level feels the same on a thin and a thick taskbar.
    // Standard is the former fixed factor: the default changes nothing. Near
    // and Far sit two thicknesses either side of it, a symmetric first guess
    // nobody has judged on screen yet: provisional until Louis checks them.
    // The fade starts (factor - 1) thicknesses before the band (Opacity), the
    // figure the page's option labels show. A number outside the enum (the converter accepts integers from a
    // hand-edited settings file) falls back to Standard.
    public static int ZoneFactor(ApproachDistance level) => level switch
    {
        ApproachDistance.Near => 2,
        ApproachDistance.Far  => 6,
        _                     => ApproachZoneFactor,
    };

    public static int Thickness(NativeMethods.RECT band, TaskbarEdge edge)
        => edge is TaskbarEdge.Left or TaskbarEdge.Right
            ? band.right - band.left
            : band.bottom - band.top;

    public static NativeMethods.RECT ApproachZone(NativeMethods.RECT band, TaskbarEdge edge)
        => ApproachZone(band, edge, ApproachDistance.Standard);

    // The approach zone spans only the band's extent along the edge: a cursor
    // near the same screen edge on another monitor stays out of it.
    public static NativeMethods.RECT ApproachZone(NativeMethods.RECT band, TaskbarEdge edge, ApproachDistance level)
    {
        int depth = ZoneFactor(level) * Thickness(band, edge);
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
    // the full span and the first pixel outside lands exactly on it.
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

    // Smoothstep over the zone's span beyond the band, the same curve as the
    // HUD's proximity fade, so both surfaces ease the same way.
    public static double Opacity(NativeMethods.RECT band, TaskbarEdge edge, POINT p)
        => Opacity(band, edge, p, ApproachDistance.Standard);

    public static double Opacity(NativeMethods.RECT band, TaskbarEdge edge, POINT p, ApproachDistance level)
    {
        if (!Contains(ApproachZone(band, edge, level), p)) return 1.0;

        int span = (ZoneFactor(level) - 1) * Thickness(band, edge);
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

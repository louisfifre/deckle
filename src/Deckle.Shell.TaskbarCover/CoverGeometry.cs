using Deckle.Core.Interop;

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

// Pure geometry of the cover band and its reveal zone — no Win32 call,
// no state. The band is exactly the taskbar rect; the reveal zone is the
// band extended inward from the anchored screen edge, so approaching
// that edge reveals the taskbar before the cursor reaches the band.
public static class CoverGeometry
{
    // Depth of the reveal zone, measured inward from the anchored screen
    // edge, in physical pixels. Includes the band thickness itself.
    // Ported from the standalone utility (EDGE_ZONE), where the value was
    // calibrated in daily use; deliberately a constant, not a setting.
    public const int RevealZoneDepth = 192;

    // The reveal zone spans the band's extent along the edge (a cursor
    // near the same screen edge on another monitor stays out of it) and
    // reaches RevealZoneDepth inward perpendicular to the edge.
    public static NativeMethods.RECT RevealZone(NativeMethods.RECT band, TaskbarEdge edge, int depth)
        => edge switch
        {
            TaskbarEdge.Left   => new() { left = band.left,           top = band.top,            right = band.left + depth, bottom = band.bottom },
            TaskbarEdge.Right  => new() { left = band.right - depth,  top = band.top,            right = band.right,        bottom = band.bottom },
            TaskbarEdge.Top    => new() { left = band.left,           top = band.top,            right = band.right,        bottom = band.top + depth },
            _                  => new() { left = band.left,           top = band.bottom - depth, right = band.right,        bottom = band.bottom },
        };

    // Half-open on right/bottom, the Win32 RECT convention.
    public static bool Contains(NativeMethods.RECT r, POINT p)
        => p.X >= r.left && p.X < r.right && p.Y >= r.top && p.Y < r.bottom;
}

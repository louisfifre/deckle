namespace Deckle.Shell.TaskbarCover;

// Whether the taskbar sits above the band in the z-order. Pure: the walk up
// the z-order is injected, so the predicate reads a fake chain in tests and
// GetWindow(GW_HWNDPREV) in the host.
public static class CoverStacking
{
    // Bound on the climb: only topmost windows can sit above a topmost band,
    // and a stack deeper than this means a corrupt or cyclic chain, not a
    // taskbar to find. Same bound as the Diagnostics z-order witness.
    public const int MaxClimb = 256;

    // Walks from the band toward the top of the z-order. True when the
    // taskbar is met on the way, false when the top is reached first or
    // either handle is missing. The taskbar directly above the band counts.
    public static bool TaskbarAbove(IntPtr band, IntPtr taskbar, Func<IntPtr, IntPtr> previous)
    {
        if (band == IntPtr.Zero || taskbar == IntPtr.Zero) return false;

        IntPtr current = previous(band);
        for (int climb = 0; current != IntPtr.Zero && climb < MaxClimb; climb++)
        {
            if (current == taskbar) return true;
            current = previous(current);
        }
        return false;
    }
}

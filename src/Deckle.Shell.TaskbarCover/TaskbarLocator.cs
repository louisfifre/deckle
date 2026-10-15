using System.Runtime.InteropServices;
using Deckle.Core;
using static Deckle.Shell.TaskbarCover.TaskbarCoverNativeMethods;

namespace Deckle.Shell.TaskbarCover;

// Finds every taskbar on the desktop and describes where each one sits. The
// one Win32 reader of the layout; everything after it is geometry on the
// placements it returns.
internal static class TaskbarLocator
{
    // The primary taskbar first, then the secondaries in desktop order. Empty
    // when the primary is missing or cannot be described (shell not ready,
    // Explorer restarting), which the host reads as "layout unknown" and
    // answers by hiding every band: a wrong band is worse than no band.
    public static List<TaskbarPlacement> Locate()
    {
        var placements = new List<TaskbarPlacement>();

        IntPtr primary = FindWindowEx(IntPtr.Zero, IntPtr.Zero, PrimaryTaskbarClass, null);
        if (primary == IntPtr.Zero || !TryDescribe(primary, out var primaryPlacement))
            return placements;
        placements.Add(primaryPlacement);

        var secondaries = new List<TaskbarPlacement>();
        for (IntPtr taskbar = FindWindowEx(IntPtr.Zero, IntPtr.Zero, SecondaryTaskbarClass, null);
             taskbar != IntPtr.Zero;
             taskbar = FindWindowEx(IntPtr.Zero, taskbar, SecondaryTaskbarClass, null))
        {
            // A hidden secondary window is a taskbar Windows is not showing,
            // nothing to cover. Whether Explorer hides or destroys these
            // windows when "Show my taskbar on all displays" goes off is
            // unverified; both cases end here. The primary carries no such
            // test: it is the layout's anchor and the host has always trusted
            // its presence alone.
            if (!IsWindowVisible(taskbar)) continue;
            if (TryDescribe(taskbar, out var placement)) secondaries.Add(placement);
        }
        secondaries.Sort(TaskbarPlacement.CompareDesktopOrder);
        placements.AddRange(secondaries);

        return placements;
    }

    private static bool TryDescribe(IntPtr taskbar, out TaskbarPlacement placement)
    {
        placement = default;

        if (!NativeMethods.GetWindowRect(taskbar, out var rect)) return false;

        IntPtr monitor = MonitorFromWindow(taskbar, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;

        placement = new TaskbarPlacement(
            taskbar, rect, CoverGeometry.EdgeOf(rect, info.rcMonitor), monitor, DpiScaleOf(monitor));
        return true;
    }

    // The monitor's effective DPI over 96. A failed query (an HRESULT other
    // than S_OK, or a zero DPI) falls back to 1.0: a zone at 96 DPI scale on
    // a scaled monitor is a shorter reach, never a wrong band.
    private static double DpiScaleOf(IntPtr monitor)
    {
        if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) != 0 || dpiX == 0)
            return 1.0;
        return dpiX / 96.0;
    }
}

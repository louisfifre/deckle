using System.IO;
using System.Runtime.InteropServices;
using Deckle.Core;
using static Deckle.Shell.Foreground.ForegroundNativeMethods;

namespace Deckle.Shell.Foreground;

// Building a snapshot: identity once per foreground switch, placement and
// fullscreen stage on every relevant location change. Each probe is a handful
// of local calls; none sends a message to another process.
public sealed partial class ForegroundWatcher
{
    private static ForegroundState ProbeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return ForegroundState.None;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        var identity = ForegroundState.None with
        {
            Hwnd = hwnd,
            ProcessId = processId,
            ProcessName = ProcessNameOf(processId),
        };
        return ProbePlacement(identity);
    }

    // Monitor and fullscreen stage of the snapshot's window, identity kept.
    private static ForegroundState ProbePlacement(ForegroundState state)
    {
        IntPtr monitor = MonitorFromWindow(state.Hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref mi))
        {
            monitor = IntPtr.Zero;
            mi = default;
        }

        return state with
        {
            Monitor = monitor,
            MonitorRect = mi.rcMonitor,
            WorkRect = mi.rcWork,
            Fullscreen = FullscreenStageOf(state.Hwnd, monitor, mi.rcMonitor),
        };
    }

    // Two stages: the shell's own notion of "busy fullscreen" first (cheap,
    // catches D3D exclusive and presentation mode), then the geometric test —
    // the window's DWM visual bounds covering its whole monitor (catches
    // borderless fullscreen and F11).
    private static FullscreenStage FullscreenStageOf(IntPtr hwnd, IntPtr monitor, NativeMethods.RECT monitorRect)
    {
        if (SHQueryUserNotificationState(out int quns) == 0
            && quns is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
        {
            return FullscreenStage.NotificationState;
        }

        if (monitor != IntPtr.Zero && !IsDesktopWindow(hwnd)
            && DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS,
                   out var bounds, (uint)Marshal.SizeOf<NativeMethods.RECT>()) == 0
            && FullscreenGeometry.CoversMonitor(bounds, monitorRect))
        {
            return FullscreenStage.Geometry;
        }

        return FullscreenStage.None;
    }

    // The desktop covers the whole monitor and would pass the geometry test
    // the moment the wallpaper is clicked. Excluded by the canonical guard
    // Windows itself uses (the shell and root desktop windows) plus the
    // WorkerW host that backs an animated or secondary wallpaper.
    private static bool IsDesktopWindow(IntPtr hwnd)
    {
        if (hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return true;

        var cls = new System.Text.StringBuilder(16);
        return NativeMethods.GetClassName(hwnd, cls, cls.Capacity) > 0
            && cls.ToString() == "WorkerW";
    }

    // Executable file name without extension — the spelling
    // Process.ProcessName uses — or "" when the process cannot be opened
    // (protected processes) or has exited. Limited query rights avoid
    // enumerating the process table on the hooking thread.
    private static string ProcessNameOf(uint processId)
    {
        if (processId == 0) return "";

        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return "";
        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size)) return "";
            return Path.GetFileNameWithoutExtension(new string(buffer, 0, (int)size));
        }
        finally
        {
            CloseHandle(process);
        }
    }
}

using System.Diagnostics;
using System.Text;

namespace Deckle.Core.Interop;

// ─── Win32 Helpers For Debugging ─────────────────────────────────────────────
//
// DescribeHwnd: produces a readable "Exe / Title / focus=Class" string to
// characterize a window. Used to diagnose focus / paste losses.

public static class Win32Util
{
    public static string DescribeHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(HWND=0)";

        try
        {
            // Exe via PID
            uint pid;
            uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            string exe = "?";
            try { exe = Process.GetProcessById((int)pid).ProcessName; } catch { }

            // Window title
            int len = NativeMethods.GetWindowTextLength(hwnd);
            string title = "";
            if (len > 0)
            {
                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                title = sb.ToString();
            }

            // Focused control in the target thread
            string focus = "?";
            var gti = new NativeMethods.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (NativeMethods.GetGUIThreadInfo(tid, ref gti))
            {
                if (gti.hwndFocus == IntPtr.Zero)
                    focus = "(none)";
                else
                {
                    var cn = new StringBuilder(128);
                    NativeMethods.GetClassName(gti.hwndFocus, cn, cn.Capacity);
                    focus = cn.ToString();
                }
            }

            return $"{exe} / \"{title}\" / focus={focus}";
        }
        catch (Exception ex)
        {
            return $"(describe err: {ex.Message})";
        }
    }

    // Process name for an HWND — just the exe, no title, no focus probe.
    // Used by the user-facing narrative log to name the target app of a paste.
    public static string GetExeName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(unknown)";
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return "(unknown)";
        }
    }
}

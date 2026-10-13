using System.Diagnostics;
using System.Text;

namespace Deckle.Core;

// ─── Win32 Helpers For Debugging ─────────────────────────────────────────────
//
// DescribeHwnd: produces a readable "Exe / Title / focus=Class" string to
// diagnose focus / paste losses.

public static class Win32Util
{
    public static string DescribeHwnd(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(HWND=0)";

        try
        {
            uint pid;
            uint tid = NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
            string exe = "?";
            try { exe = Process.GetProcessById((int)pid).ProcessName; } catch { }

            int len = NativeMethods.GetWindowTextLength(hwnd);
            string title = "";
            if (len > 0)
            {
                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                title = sb.ToString();
            }

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

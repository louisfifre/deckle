using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Text;

namespace Deckle.Diagnostics;

// Internal helper for the Diagnostics module: factors the Win32 P/Invokes
// (`GetWindowRect`, `GetDpiForWindow`, `MonitorFromWindow`) consumed by the
// seven window-positioning sites wired to `DeckleWindowingSource`. Without this
// helper, each site would duplicate the four P/Invoke lines + parameter
// construction; seven sites would multiply the instrumentation debt by seven.
//
// **No dependency on `Deckle.Core`.** The Diagnostics module sits beneath every
// other technical brick (see `CLAUDE.md`); the required P/Invokes are
// redeclared locally (private) instead of introducing a hard dependency on
// `Deckle.Core.Interop.NativeMethods`. No symbol overlap: P/Invoke
// declarations are local to this file, and `Deckle.Core` keeps its own
// declarations for the rest of the app.
//
// **Strict gate before any cost.** Each `Emit*` method tests
// `IsEnabled(Verbose, Windowing)` at the top: when no listener is attached, the
// instrumentation has zero net cost (one ETW test + return). P/Invokes are
// never called if the gate is closed.
//
// **Absolute screen pixel convention.** `GetWindowRect` already returns
// absolute screen pixels (unlike `AppWindow.Position`/`Size`, which stay in
// pixels but are tied to the post-Move `AppWindow`). Reading the
// post-positioning rect directly guarantees we capture the effective DWM-side
// state, not the pre-Move intent.
public static class WindowingProbe
{
    // ── Private P/Invokes ───────────────────────────────────────────────

    // Window rect in absolute screen pixels; includes the non-client area
    // (frame, title). For Deckle windows that remove the NC area through
    // WM_NCCALCSIZE (HUD, HudOverlay), it is equivalent to the client rect.
    // For classic app windows (Settings, Log, Setup), the NC gap is marginal
    // (standard frame ~1 dip + caption).
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    // Logical window DPI (96 = 100%, 120 = 125%, 144 = 150%...).
    // Per-monitor DPI aware: follows the monitor the window is on, changes at
    // runtime on cross-monitor drag.
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // Monitor handle for the monitor the window is on. `dwFlags=2`
    // (MONITOR_DEFAULTTONEAREST) guarantees a monitor is always returned even
    // if the window is partially off-screen (runtime case after resolution
    // change or display disconnect).
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(
        IntPtr hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x00000008;
    private const uint GW_HWNDPREV = 3;
    private const uint DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    internal readonly record struct WindowRect(int Left, int Top, int Right, int Bottom)
    {
        public bool IsEmpty => Right <= Left || Bottom <= Top;

        public bool Intersects(WindowRect other)
            => !IsEmpty && !other.IsEmpty
                && Left < other.Right
                && Right > other.Left
                && Top < other.Bottom
                && Bottom > other.Top;
    }

    internal readonly record struct ZOrderWindow(
        long Hwnd,
        long Pid,
        string ClassName,
        bool Visible,
        bool Topmost,
        bool Cloaked,
        WindowRect Rect);

    internal readonly record struct ZOrderAboveSummary(
        int VisibleCount,
        long FirstVisiblePid,
        string FirstVisibleClassName,
        bool FirstVisibleTopmost,
        int OccludingCount,
        long FirstOccludingPid,
        string FirstOccludingClassName,
        bool FirstOccludingTopmost);

    // ── Emission Helpers ────────────────────────────────────────────────

    // Emits the `WindowPositioned` common trunk for every site that positions
    // or resizes a window whose HWND is owned by the app. `window` is a short
    // logical name from the closed vocabulary (see
    // `DeckleWindowingSource.WindowPositioned` docs), and `anchor` describes
    // the code-side placement intent.
    public static void EmitWindowPositioned(IntPtr hwnd, string window, string anchor)
    {
        if (!DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        if (hwnd == IntPtr.Zero) return;

        if (!GetWindowRect(hwnd, out var rect)) return;
        int dpi = (int)GetDpiForWindow(hwnd);
        long hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST).ToInt64();

        DeckleWindowingSource.Log.WindowPositioned(
            window, hmon, dpi, anchor,
            rect.left, rect.top,
            rect.right - rect.left, rect.bottom - rect.top);
    }

    // Emits `OverlaySlotAssigned` (stacking specialization) in addition to the
    // common trunk already emitted by EmitWindowPositioned. Deckle overlays
    // (`HudOverlayWindow`) each have a 0..N-1 slot assigned by
    // `HudOverlayManager.Recompact`: slot 0 = closest to the main HUD, slot
    // N-1 = farthest away.
    public static void EmitOverlaySlotAssigned(IntPtr hwnd, int slot)
    {
        if (!DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        if (hwnd == IntPtr.Zero) return;

        if (!GetWindowRect(hwnd, out var rect)) return;
        long hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST).ToInt64();

        DeckleWindowingSource.Log.OverlaySlotAssigned(
            slot, hmon,
            rect.left, rect.top,
            rect.right - rect.left, rect.bottom - rect.top);
    }

    // Emits `PopupAnchored` (parent anchoring specialization) in addition to
    // the common trunk. `popup` is a short logical name ("tray-popup",
    // "folder-picker"), and `parent_rect_x/y/w/h` describe the anchored
    // control rect (tray icon, picker button) in absolute screen pixels. For
    // popups whose HWND is not owned by the app (native TrackPopupMenu menu,
    // system FolderPicker dialog), pass `IntPtr.Zero` as hwnd; effective
    // position/size is then emitted as zero and only the intent (parent_rect)
    // is traced.
    public static void EmitPopupAnchored(
        IntPtr hwnd, string popup,
        int parent_rect_x, int parent_rect_y,
        int parent_rect_w, int parent_rect_h)
    {
        if (!DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;

        int pos_x = 0, pos_y = 0, size_w = 0, size_h = 0;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rect))
        {
            pos_x  = rect.left;
            pos_y  = rect.top;
            size_w = rect.right - rect.left;
            size_h = rect.bottom - rect.top;
        }

        string parent_rect = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{parent_rect_x},{parent_rect_y},{parent_rect_w},{parent_rect_h}");

        DeckleWindowingSource.Log.PopupAnchored(
            popup, parent_rect, pos_x, pos_y, size_w, size_h);
    }

    public static void EmitWindowZOrderState(
        IntPtr hwnd, string window, string stage,
        bool setposOk = true, int lastError = 0)
    {
        if (!DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing)) return;
        if (hwnd == IntPtr.Zero) return;

        bool visible = IsWindowVisible(hwnd);
        bool topmost = HasTopmostStyle(hwnd);

        uint foregroundPid = 0;
        string foregroundClass = "";
        IntPtr foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero)
        {
            GetWindowThreadProcessId(foreground, out foregroundPid);
            foregroundClass = GetClassNameOrEmpty(foreground);
        }

        uint previousPid = 0;
        bool previousVisible = false;
        bool previousTopmost = false;
        string previousClass = "";
        IntPtr previous = GetWindow(hwnd, GW_HWNDPREV);
        if (previous != IntPtr.Zero)
        {
            GetWindowThreadProcessId(previous, out previousPid);
            previousVisible = IsWindowVisible(previous);
            previousTopmost = HasTopmostStyle(previous);
            previousClass = GetClassNameOrEmpty(previous);
        }

        WindowRect targetRect = TryGetWindowRect(hwnd, out var rect)
            ? rect
            : new WindowRect(0, 0, 0, 0);
        var above = SummarizeWindowsAbove(hwnd, targetRect);

        DeckleWindowingSource.Log.WindowZOrderState(
            window, stage, visible, topmost,
            previousVisible, previousTopmost,
            foregroundPid, foregroundClass,
            previous.ToInt64(), previousPid, previousClass,
            above.VisibleCount,
            above.FirstVisiblePid,
            above.FirstVisibleClassName,
            above.FirstVisibleTopmost,
            above.OccludingCount,
            above.FirstOccludingPid,
            above.FirstOccludingClassName,
            above.FirstOccludingTopmost,
            setposOk, lastError);
    }

    private static bool HasTopmostStyle(IntPtr hwnd)
    {
        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        return (exStyle & WS_EX_TOPMOST) != 0;
    }

    private static string GetClassNameOrEmpty(IntPtr hwnd)
    {
        var sb = new StringBuilder(128);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    internal static ZOrderAboveSummary SummarizeWindowsAboveForTest(
        WindowRect targetRect,
        IReadOnlyList<ZOrderWindow> windowsAbove)
        => SummarizeWindowsAbove(targetRect, windowsAbove);

    private static ZOrderAboveSummary SummarizeWindowsAbove(IntPtr hwnd, WindowRect targetRect)
    {
        var windowsAbove = new List<ZOrderWindow>();
        IntPtr current = GetWindow(hwnd, GW_HWNDPREV);
        int guard = 0;
        while (current != IntPtr.Zero && guard++ < 256)
        {
            if (TryReadZOrderWindow(current, out var candidate))
            {
                windowsAbove.Add(candidate);
            }

            current = GetWindow(current, GW_HWNDPREV);
        }

        return SummarizeWindowsAbove(targetRect, windowsAbove);
    }

    private static ZOrderAboveSummary SummarizeWindowsAbove(
        WindowRect targetRect,
        IReadOnlyList<ZOrderWindow> windowsAbove)
    {
        int visibleCount = 0;
        long firstVisiblePid = 0;
        string firstVisibleClass = "";
        bool firstVisibleTopmost = false;

        int occludingCount = 0;
        long firstOccludingPid = 0;
        string firstOccludingClass = "";
        bool firstOccludingTopmost = false;

        foreach (var current in windowsAbove)
        {
            if (current.Visible)
            {
                visibleCount++;
                if (firstVisiblePid == 0)
                {
                    firstVisiblePid = current.Pid;
                    firstVisibleClass = current.ClassName;
                    firstVisibleTopmost = current.Topmost;
                }
            }

            if (IsOccludingTarget(targetRect, current))
            {
                occludingCount++;
                if (firstOccludingPid == 0)
                {
                    firstOccludingPid = current.Pid;
                    firstOccludingClass = current.ClassName;
                    firstOccludingTopmost = current.Topmost;
                }
            }
        }

        return new ZOrderAboveSummary(
            visibleCount,
            firstVisiblePid,
            firstVisibleClass,
            firstVisibleTopmost,
            occludingCount,
            firstOccludingPid,
            firstOccludingClass,
            firstOccludingTopmost);
    }

    private static bool IsOccludingTarget(WindowRect targetRect, ZOrderWindow candidate)
        => candidate.Visible
            && !candidate.Cloaked
            && candidate.Rect.Intersects(targetRect);

    private static bool TryReadZOrderWindow(IntPtr hwnd, out ZOrderWindow window)
    {
        window = default;
        if (!TryGetWindowRect(hwnd, out var rect)) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        window = new ZOrderWindow(
            hwnd.ToInt64(),
            pid,
            GetClassNameOrEmpty(hwnd),
            IsWindowVisible(hwnd),
            HasTopmostStyle(hwnd),
            IsCloaked(hwnd),
            rect);
        return true;
    }

    private static bool TryGetWindowRect(IntPtr hwnd, out WindowRect rect)
    {
        rect = default;
        if (!GetWindowRect(hwnd, out var nativeRect)) return false;
        rect = new WindowRect(nativeRect.left, nativeRect.top, nativeRect.right, nativeRect.bottom);
        return true;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        int hr = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }
}

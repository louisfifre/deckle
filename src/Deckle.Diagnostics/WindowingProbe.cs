using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Text;

namespace Deckle.Diagnostics;

// Factors the Win32 reads every window-positioning site needs for
// `DeckleWindowingSource`.
//
// **No dependency on `Deckle.Core`.** The Diagnostics module sits beneath every
// other technical brick, so the required P/Invokes are redeclared locally
// (private) instead of referencing `Deckle.Core.NativeMethods`.
//
// **Strict gate before any cost.** Each `Emit*` method checks the Windowing
// admission and the listener before any P/Invoke. The `Report*` methods are
// the exception: they carry failures, which no verbosity control silences.
//
// Reading `GetWindowRect` after positioning captures the effective DWM-side
// state, not the requested one.
public static class WindowingProbe
{
    // ── Private P/Invokes ───────────────────────────────────────────────

    // Includes the non-client frame; it equals the client rect only for windows
    // that erase it through WM_NCCALCSIZE (HUD, HudOverlay).
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

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

    // For windows whose HWND the app owns.
    public static void EmitWindowPositioned(IntPtr hwnd, string window, string anchor)
    {
        if (!OperationalLogAdmission.IsEnabled(OperationalLogActivity.Windowing)) return;
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

    public static void EmitOverlaySlotAssigned(IntPtr hwnd, int slot)
    {
        if (!OperationalLogAdmission.IsEnabled(OperationalLogActivity.Windowing)) return;
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

    // For a popup whose HWND the app does not own (system FolderPicker
    // dialog), pass `IntPtr.Zero`: position and size are then emitted as zero
    // and only the anchoring intent (parent_rect) is traced.
    public static void EmitPopupAnchored(
        IntPtr hwnd, string popup,
        int parent_rect_x, int parent_rect_y,
        int parent_rect_w, int parent_rect_h)
    {
        if (!OperationalLogAdmission.IsEnabled(OperationalLogActivity.Windowing)) return;
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
        if (!OperationalLogAdmission.IsEnabled(OperationalLogActivity.Windowing)) return;
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

    // ── Failure incidents ───────────────────────────────────────────────
    //
    // A show or placement call can keep failing: the taskbar cover re-asserts
    // topmost on every foreground change, the HUD shows on every dictation.
    // One (window, stage) pair is one incident. Its first failure is the
    // Warning, because a window that did not appear is already visible to the
    // person; the repeats are governed Verbose; the first success closes it
    // with one Informational line. Call sites report every outcome, success
    // included, so the incident can close.
    //
    // Not gated on the Windowing activity: failures always reach the log.

    private static readonly object _incidentGate = new();
    private static readonly Dictionary<(string Window, string Stage), int> _openIncidents = new();

    // Lets a success skip the lock while no incident is open, the common case
    // on the per-show and per-foreground-change paths.
    private static int _openIncidentCount;

    // For a Win32 call: pass its BOOL result and, on failure,
    // Marshal.GetLastWin32Error() read right after the call.
    public static void ReportShow(string window, string stage, bool ok, int win32Error = 0)
        => Report(show: true, window, stage, ok, hr: 0, win32Error, exType: "", exMessage: "");

    public static void ReportShowFailure(string window, string stage, Exception exception)
        => Report(show: true, window, stage, ok: false,
            exception.HResult, win32Error: 0, exception.GetType().Name, exception.Message);

    public static void ReportPlacement(string window, string stage, bool ok, int win32Error = 0)
        => Report(show: false, window, stage, ok, hr: 0, win32Error, exType: "", exMessage: "");

    public static void ReportPlacementFailure(string window, string stage, Exception exception)
        => Report(show: false, window, stage, ok: false,
            exception.HResult, win32Error: 0, exception.GetType().Name, exception.Message);

    private static void Report(
        bool show, string window, string stage, bool ok,
        int hr, int win32Error, string exType, string exMessage)
    {
        var key = (window, stage);

        if (ok)
        {
            if (Volatile.Read(ref _openIncidentCount) == 0) return;
            int recoveredFailures;
            lock (_incidentGate)
            {
                if (!_openIncidents.Remove(key, out recoveredFailures)) return;
                Volatile.Write(ref _openIncidentCount, _openIncidents.Count);
            }
            DeckleWindowingSource.Log.WindowFailureRecovered(window, stage, recoveredFailures);
            return;
        }

        int failures;
        lock (_incidentGate)
        {
            _openIncidents.TryGetValue(key, out failures);
            failures++;
            _openIncidents[key] = failures;
            Volatile.Write(ref _openIncidentCount, _openIncidents.Count);
        }

        if (failures > 1)
        {
            DeckleWindowingSource.Log.WindowFailureRepeated(window, stage, failures, hr, win32Error);
        }
        else if (show)
        {
            DeckleWindowingSource.Log.WindowShowFailed(window, stage, hr, win32Error, exType, exMessage);
        }
        else
        {
            DeckleWindowingSource.Log.WindowPlacementFailed(window, stage, hr, win32Error, exType, exMessage);
        }
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

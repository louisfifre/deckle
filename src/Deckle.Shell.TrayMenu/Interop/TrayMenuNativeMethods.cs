using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell.TrayMenu;

// ─── Tray menu specific P/Invokes ─────────────────────────────────────────────
//
// Generic imports (GetCursorPos, SetForegroundWindow, ShowWindow,
// SetWindowLongPtr, SetLayeredWindowAttributes, DwmSetWindowAttribute,
// GetDpiForWindow…) live in Deckle.Core.NativeMethods and are consumed
// as-is. Here we only add what is missing: native popup positioner and related
// style constants.

internal static class TrayMenuNativeMethods
{
    // ── CalculatePopupWindowPosition ──────────────────────────────────────────
    //
    // user32 API that computes the canonical popup position given an anchor
    // point, window size, alignment flags, and exclusion rect. This is exactly
    // the calculation TrackPopupMenu does internally, exposed separately for
    // custom popup implementations. Accounts for taskbar and monitor bounds
    // when TPM_WORKAREA is passed. Returns the position through
    // popupWindowPosition, not the return value, which is only BOOL
    // success/failure.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CalculatePopupWindowPosition(
        ref POINT anchorPoint,
        ref SIZE windowSize,
        uint flags,
        ref NativeMethods.RECT excludeRect,
        ref NativeMethods.RECT popupWindowPosition);

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    // ── Additional TPM_* flags ────────────────────────────────────────────────
    //
    // TPM_BOTTOMALIGN / TPM_RIGHTALIGN already live in Deckle.Core.
    // TPM_WORKAREA constrains the popup to the current monitor work area
    // (excluding the taskbar). Essential so a tray menu does not overflow under
    // the taskbar.

    public const uint TPM_WORKAREA = 0x10000;
    public const uint TPM_VERTICAL = 0x0040;

    // ── Additional window styles ──────────────────────────────────────────────
    //
    // WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU. Applied post-Loaded
    // to the host handle to clear any WinUI 3 caption trace inherited from
    // OverlappedPresenter and present a pure popup HWND to DWM (no system
    // border, no system menu, no titlebar).

    public const uint WS_POPUP       = 0x80000000;
    public const uint WS_BORDER      = 0x00800000;
    public const uint WS_SYSMENU     = 0x00080000;
    public const uint WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU;

    // ── Additional ShowWindow nCmdShow values ────────────────────────────────
    //
    // SW_HIDE and SW_SHOWNOACTIVATE already live in Deckle.Core.
    // SW_SHOWNORMAL activates the window; required so it receives the focus
    // that SetForegroundWindow will then confirm. Without activation,
    // MenuFlyout does not dismiss correctly on click-outside.

    public const int SW_SHOWNORMAL = 1;

    // ── DPI per-monitor ───────────────────────────────────────────────────────
    //
    // The scale applied to the flyout must reflect the DPI of the monitor under
    // the cursor, not the monitor where the carrier window lives (it is hidden
    // at boot on the primary monitor). The frame's `XamlRoot.RasterizationScale`
    // returns the scale of that primary monitor, so it is wrong on
    // multi-monitor setups or if the primary screen is not at 100%. Resolve
    // properly with MonitorFromPoint(cursor) + GetDpiForMonitor.

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const int MDT_EFFECTIVE_DPI = 0;
}

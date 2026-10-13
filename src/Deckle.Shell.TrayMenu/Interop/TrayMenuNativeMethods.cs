using System.Runtime.InteropServices;
using Deckle.Core;

namespace Deckle.Shell.TrayMenu;

// ─── Tray menu specific P/Invokes ─────────────────────────────────────────────
//
// Generic imports live in Deckle.Core.NativeMethods; only what it lacks is here.

internal static class TrayMenuNativeMethods
{
    // ── CalculatePopupWindowPosition ──────────────────────────────────────────
    //
    // The calculation TrackPopupMenu does internally, exposed for custom
    // popups. The position comes back through popupWindowPosition; the
    // return value is only success or failure.
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
    // TPM_WORKAREA constrains the popup to the monitor work area, so a tray
    // menu does not overflow under the taskbar.

    public const uint TPM_WORKAREA = 0x10000;
    public const uint TPM_VERTICAL = 0x0040;

    // ── Additional window styles ──────────────────────────────────────────────

    public const uint WS_POPUP       = 0x80000000;
    public const uint WS_BORDER      = 0x00800000;
    public const uint WS_SYSMENU     = 0x00080000;
    public const uint WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU;

    // ── Additional ShowWindow nCmdShow values ────────────────────────────────
    //
    // SW_SHOWNORMAL activates the window, so it receives the focus that
    // SetForegroundWindow then confirms. Without activation, MenuFlyout does
    // not dismiss correctly on click-outside.

    public const int SW_SHOWNORMAL = 1;

    // ── DPI per-monitor ───────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const int MDT_EFFECTIVE_DPI = 0;
}

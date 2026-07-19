using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deckle.Core;

public static partial class NativeMethods
{
    // ── Window Positioning ───────────────────────────────────────────────────

    public static readonly IntPtr HWND_TOP      = new(0);
    public static readonly IntPtr HWND_TOPMOST  = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    public const uint SWP_NOSIZE       = 0x0001;
    public const uint SWP_NOMOVE       = 0x0002;
    public const uint SWP_NOZORDER     = 0x0004;
    public const uint SWP_NOACTIVATE   = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW   = 0x0040;

    // SW_SHOWNOACTIVATE: shows the window without giving it focus.
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_HIDE           = 0;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // ── Window Focus ─────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // ── Window Identification / Keyboard Focus (debug) ───────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int    cbSize;
        public uint   flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT   rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    // Returns the logical window DPI (96 = 100%, 120 = 125%, 144 = 150%...).
    // Per-monitor DPI aware: follows the monitor the window is on.
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    // ── SetWindowSubclass (comctl32 v6) ───────────────────────────────────────
    // Requires Common Controls v6 in app.manifest.
    // Do not use SetWindowLongPtr(GWLP_WNDPROC): it would entirely replace the
    // WinUI 3 message chain and break the compositor.

    public delegate IntPtr SubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool SetWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass,
        UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool RemoveWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass,
        UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    public static extern IntPtr DefSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    // ── Resize / move modal loop (WM_ENTERSIZEMOVE … WM_EXITSIZEMOVE) ─────────
    //
    // When the user grabs the title bar or a sizing border, Windows runs a modal
    // move/size loop: WM_ENTERSIZEMOVE once at the start, a burst of WM_SIZE /
    // WM_SIZING while the pointer moves, then WM_EXITSIZEMOVE once when
    // DefWindowProc returns. Maximize, snap and programmatic SetWindowPos do NOT
    // enter this loop — they emit WM_SIZE alone. A resize coalescer brackets the
    // gesture on ENTER/EXIT and treats a WM_SIZE seen outside it as a one-shot
    // (the safety net). See Deckle.Shell/ResizeCoalescer.
    public const uint WM_SIZE          = 0x0005;
    public const uint WM_ENTERSIZEMOVE = 0x0231;
    public const uint WM_EXITSIZEMOVE  = 0x0232;

    // WM_SIZE wParam value when the window was minimized (client area collapses
    // to 0×0). A coalescer ignores it so a minimize triggers no phantom recompute.
    public const int SIZE_MINIMIZED    = 1;

    // ── Layered Window (global alpha, Mica included) ─────────────────────────
    // WS_EX_LAYERED + SetLayeredWindowAttributes(LWA_ALPHA) applies a 0-255
    // alpha to the whole window, over WinUI 3 composition (Mica included).
    // Without it, animating Content.Opacity does not affect the backdrop.

    public const int  GWL_STYLE    = -16;
    public const int  GWL_EXSTYLE   = -20;

    // WS_CAPTION is the composite style (WS_BORDER | WS_DLGFRAME) that causes
    // Windows to paint a title bar *and* the thin frame around the client
    // area. OverlappedPresenter.SetBorderAndTitleBar(false, false) is supposed
    // to clear both bits but does not fully — the frame around the client
    // area remains visible, which reads as a rough XP-style outline on
    // WS_EX_LAYERED overlays. Stripping WS_CAPTION explicitly on GWL_STYLE is
    // the documented Win32 workaround (Microsoft Q&A 1300756, WinUIEx #134,
    // WindowsAppSDK #3622).
    public const uint WS_CAPTION    = 0x00C00000;
    public const uint WS_THICKFRAME = 0x00040000;

    public const uint WS_EX_LAYERED    = 0x00080000;
    // WS_EX_TOOLWINDOW: excludes the window from Alt+Tab and the taskbar.
    // Observed side effect (undocumented): topmost tool windows appear on all
    // virtual desktops. This is the mechanism PowerToys uses for its overlays.
    // Best-effort; may break on future Windows builds.
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    // WS_EX_TRANSPARENT: excludes the window from hit-testing. Clicks, cursor,
    // and selection pass through the HUD and reach the window below,
    // regardless of the applied layered alpha.
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    // WS_EX_NOACTIVATE: the window does not become foreground when shown or
    // clicked. Aligned with the HUD contract: visible information, never focus
    // stealing. Future interactive menus must live on a separate surface, not
    // on this passthrough window.
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    // WS_EX_TOPMOST: born above all non-topmost windows, kept there even when
    // deactivated. Set at creation deliberately — a post-creation
    // SetWindowPos(HWND_TOPMOST) requires the owning process to hold
    // SetForegroundWindow permission, which it lacks while another app owns the
    // foreground; the call then returns success but silently drops the topmost
    // promotion. Creation carries no such gate.
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint LWA_ALPHA     = 0x00000002;

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(
        IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    // ── DWM window attributes ─────────────────────────────────────────────────
    //
    // DWMWA_WINDOW_CORNER_PREFERENCE (33) controls whether DWM clips the HWND
    // to rounded corners at the compositor level. DWMWA_BORDER_COLOR (34)
    // controls the 1-dip system accent stroke DWM paints around the HWND.
    // DWMWA_COLOR_NONE (0xFFFFFFFE) is the sentinel that disables that stroke.

    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint DWMWCP_DEFAULT                 = 0;
    public const uint DWMWCP_DONOTROUND              = 1;
    public const uint DWMWCP_ROUND                   = 2;
    public const uint DWMWCP_ROUNDSMALL              = 3;

    public const uint DWMWA_BORDER_COLOR  = 34;
    public const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF; // system default accent stroke
    public const uint DWMWA_COLOR_NONE    = 0xFFFFFFFE; // disables the stroke entirely

    // DWMWA_SYSTEMBACKDROP_TYPE (38) controls the DWM system backdrop layer
    // rendered behind the window (Mica / Acrylic / Tabbed). DWMSBT_NONE (1)
    // explicitly disables the backdrop — distinct from DWMSBT_AUTO (0) which
    // lets the OS pick. WinUI 3 may auto-apply a backdrop when the Window's
    // SystemBackdrop property is unset on recent WindowsAppSDK versions;
    // setting the DWM attribute is the Win32-side guarantee that nothing
    // paints behind our opaque content.
    public const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const uint DWMSBT_AUTO              = 0;
    public const uint DWMSBT_NONE              = 1;

    // DWMWA_NCRENDERING_POLICY (2) tells DWM whether to render non-client
    // decorations (frame, 1-dip accent stroke, Shell dropshadow around
    // rounded corners) for the window. DWMNCRP_DISABLED (1) turns all of
    // that off — needed on the overlay cards so their Win11 rounded-corner
    // Shell shadow stops bleeding down onto the main HUD sitting 12 dip
    // below. The DWMWCP_ROUND corner clipping is a separate compositor-
    // level attribute and keeps working with NC rendering disabled.
    public const uint DWMWA_NCRENDERING_POLICY = 2;
    public const uint DWMNCRP_USEWINDOWSTYLE   = 0;
    public const uint DWMNCRP_DISABLED         = 1;
    public const uint DWMNCRP_ENABLED          = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(
        IntPtr hwnd, uint dwAttribute, ref uint pvAttribute, uint cbAttribute);

}

using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deckle.Core;

public static partial class NativeMethods
{
    // ── Tray Icon (Shell32) ──────────────────────────────────────────────────

    public const uint NIM_ADD    = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON    = 0x00000002;
    public const uint NIF_TIP     = 0x00000004;

    public const uint WM_TRAY = 0x0400 + 1; // WM_USER + 1

    public const uint WM_RBUTTONUP      = 0x0205;
    public const uint WM_LBUTTONUP      = 0x0202;
    public const uint WM_LBUTTONDBLCLK  = 0x0203;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    // Returns the tray icon rect in physical pixels (screen coordinates),
    // identified by (hWnd, uID). Vista+. The returned HRESULT is S_OK (0) on
    // success; any negative value indicates failure (missing icon, hidden in
    // overflow, or shell busy during an explorer.exe restart).
    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(
        ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    // ── Icon (user32 / LoadImage) ────────────────────────────────────────────

    public const uint IMAGE_ICON      = 1;
    public const uint LR_LOADFROMFILE = 0x00000010;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadImage(
        IntPtr hInst, string lpszName, uint uType,
        int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    // ── Context Menu (user32) ────────────────────────────────────────────────

    public const uint MF_STRING    = 0x00000000;
    public const uint MF_CHECKED   = 0x00000008;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_GRAYED    = 0x00000001;

    public const uint TPM_LEFTBUTTON = 0x0000;
    public const uint TPM_RETURNCMD  = 0x0100;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_RIGHTALIGN  = 0x0008;

    public const uint WM_COMMAND = 0x0111;

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    public static extern uint TrackPopupMenu(
        IntPtr hMenu, uint uFlags,
        int x, int y, int nReserved,
        IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

}

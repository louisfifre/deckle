using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deckle.Core;

public static partial class NativeMethods
{
    // ── Hotkey ────────────────────────────────────────────────────────────────

    public const int WM_HOTKEY = 0x0312;

    public const int WM_INPUTLANGCHANGE = 0x0051;

    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CONTROL  = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_WIN      = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000; // avoids repeated WM_HOTKEY from keyboard auto-repeat

    // uMapType for MapVirtualKeyEx — scancode → virtual-key (distinguishing
    // left/right shift/ctrl/alt, which MAPVK_VSC_TO_VK does not).
    // Official value is 3, not 4 (4 is MAPVK_VK_TO_VSC_EX, the inverse mapping).
    public const uint MAPVK_VSC_TO_VK_EX = 3;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint MapVirtualKeyExW(uint uCode, uint uMapType, IntPtr dwhkl);

    // idThread == 0 returns the calling thread's layout, not the foreground
    // window's.
    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

}

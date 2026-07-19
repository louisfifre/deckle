using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deckle.Core;

public static partial class NativeMethods
{
    // ── Hotkey ────────────────────────────────────────────────────────────────

    public const int WM_HOTKEY = 0x0312;

    // Sent to the focused window when the active input language changes
    // (layout switch via Win+Space, language bar, etc.). We intercept it in
    // the hotkey host subclass to re-resolve the VK for the "left of 1" key
    // and re-register all hotkeys under the new layout.
    public const int WM_INPUTLANGCHANGE = 0x0051;

    public const uint MOD_ALT      = 0x0001;
    public const uint MOD_CONTROL  = 0x0002;
    public const uint MOD_SHIFT    = 0x0004;
    public const uint MOD_WIN      = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000; // avoids repeated WM_HOTKEY from keyboard auto-repeat

    // Scancode of the physical key to the left of "1" on every ANSI/ISO 104
    // keyboard. Stable across layouts — only the VK it maps to changes
    // (backtick on QWERTY US, ² on AZERTY FR, etc.). Resolve the current VK
    // via MapVirtualKeyExW(SC, MAPVK_VSC_TO_VK_EX, GetKeyboardLayout(0)).
    public const uint SC_LEFT_OF_ONE = 0x29;

    // uMapType for MapVirtualKeyEx — scancode → virtual-key (distinguishing
    // left/right shift/ctrl/alt, which MAPVK_VSC_TO_VK does not).
    // Official value is 3, not 4 (4 is MAPVK_VK_TO_VSC_EX, the inverse mapping).
    public const uint MAPVK_VSC_TO_VK_EX = 3;

    public const int HOTKEY_ID_TRANSCRIBE        = 1; // Win+[left-of-1]
    public const int HOTKEY_ID_PRIMARY_REWRITE   = 2; // Shift+Win+[left-of-1]
    public const int HOTKEY_ID_SECONDARY_REWRITE = 3; // Ctrl+Win+[left-of-1]

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Translates a scancode/virtual-key via a specific keyboard layout (HKL).
    // Needed for layout portability: at registration time we resolve the VK
    // for SC_LEFT_OF_ONE under the *current* HKL, so the physical key is
    // always matched regardless of the active layout.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint MapVirtualKeyExW(uint uCode, uint uMapType, IntPtr dwhkl);

    // Returns the HKL (keyboard layout handle) of the specified thread, or
    // of the active thread when idThread == 0.
    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

}

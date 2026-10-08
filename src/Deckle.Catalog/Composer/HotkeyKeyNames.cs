using System;
using System.Runtime.InteropServices;

namespace Deckle.Catalog;

// The name the active keyboard layout gives a scancode, for display only.
//
// GetKeyNameTextW reads the scancode from bits 16-23 of a WM_KEYDOWN-shaped
// lParam and the extended-key flag from bit 24, so a HotkeyChord scancode
// (0xE000 | sc for extended keys) is unpacked into that form. The name follows
// the layout of the calling thread: the key left of "1" reads ` on QWERTY US
// and ² on AZERTY FR, which is what the user sees on the keycap.
internal static class HotkeyKeyNames
{
    private const uint ExtendedPrefix = 0xE000;

    public static string Get(uint scanCode)
    {
        int lParam = (int)((scanCode & 0xFF) << 16);
        if ((scanCode & 0xFF00) == ExtendedPrefix) lParam |= 1 << 24;

        var buffer = new char[64];
        int length = GetKeyNameTextW(lParam, buffer, buffer.Length);

        // Zero means the layout has no name for this scancode: show the raw code
        // rather than an empty button.
        return length > 0 ? new string(buffer, 0, length) : $"Key 0x{scanCode:X2}";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetKeyNameTextW(int lParam, [Out] char[] lpString, int cchSize);
}

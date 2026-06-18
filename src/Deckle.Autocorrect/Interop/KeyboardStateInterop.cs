using System.Runtime.InteropServices;
using System.Text;

namespace Deckle.Autocorrect;

// The user32 entry points the KeyDecoder needs to turn a virtual-key into
// the character(s) it would produce, under the foreground window's layout.
// GetForegroundWindow / GetWindowThreadProcessId already live in
// Deckle.Core.NativeMethods — they are reused, not redeclared here.
internal static class KeyboardStateInterop
{
    // ToUnicodeEx maps a virtual-key + scancode + a 256-byte keyboard state
    // to the Unicode it produces under a given HKL. Return value:
    //   > 0  number of chars written to pwszBuff
    //     0  no translation (the key has no character for this state)
    //    -1  a dead key (its glyph stays pending in the kernel buffer)
    // wFlags bit 2 (0x4, since Win10 1607) tells the kernel NOT to mutate
    // its own dead-key state — mandatory for an observer that must never
    // disturb the user's live composition.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int ToUnicodeEx(
        uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    // Active keyboard layout (HKL) of a given thread; 0 = the calling thread.
    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    // Async state of a virtual-key. We only use the toggle bit (& 1) of
    // VK_CAPITAL at construction to seed CapsLock; the high bit (down/up) is
    // unreliable here since the host thread is not the foreground thread.
    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);
}

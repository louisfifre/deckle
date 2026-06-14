using System.Text;
using Deckle.Core;
using Deckle.Input.Autocorrect;
using Deckle.Input;

namespace Deckle.Input.Autocorrect;

// Turns the raw KeyboardKeyEvent stream into Keystroke?, classifying each
// key-down and resolving its character through ToUnicodeEx.
//
// It maintains its OWN modifier state from the raw down/up transitions. The
// host thread is not the foreground thread, so GetKeyboardState would report
// the host's idle modifiers, not the typist's — the only trustworthy source
// is the stream we are already observing. Caps lock is the one exception:
// its TOGGLE is a system-wide latch with no per-thread view, so it is seeded
// once from GetKeyState(VK_CAPITAL) & 1 and flipped on every VK_CAPITAL down.
public sealed class KeyDecoder
{
    // ToUnicodeEx injected so tests can drive classification without a live
    // layout. Returns the ToUnicodeEx code (>0 chars / 0 none / -1 dead key)
    // and fills `buffer` with the produced chars.
    internal delegate int ToUnicodeFn(ushort vk, ushort scanCode, byte[] keyState, StringBuilder buffer);

    // ── Virtual-key constants (winuser.h) ──
    private const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const ushort VK_CAPITAL = 0x14, VK_ESCAPE = 0x1B;
    private const ushort VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24;
    private const ushort VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const ushort VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
    private const ushort VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    private const uint TOUNICODE_NOSTATECHANGE = 0x4; // wFlags bit 2 — observer must not touch the dead-key buffer

    private readonly ToUnicodeFn _toUnicode;

    // Side-specific modifier latches, driven by down/up of both the generic
    // VK (0x10/0x11/0x12) and the sided VK (0xA0..0xA5). The generic and sided
    // forms can both arrive depending on the source; tracking each independently
    // and OR-ing them keeps "shift is down" true as long as either is held.
    private bool _lShift, _rShift, _genShift;
    private bool _lCtrl, _rCtrl, _genCtrl;
    private bool _lAlt, _rAlt, _genAlt;
    private bool _lWin, _rWin;
    private bool _capsToggled;

    private bool ShiftDown => _lShift || _rShift || _genShift;
    private bool CtrlDown => _lCtrl || _rCtrl || _genCtrl;
    private bool AltDown => _lAlt || _rAlt || _genAlt;
    private bool WinDown => _lWin || _rWin;

    public KeyDecoder()
    {
        _toUnicode = RealToUnicode;
        _capsToggled = (KeyboardStateInterop.GetKeyState(VK_CAPITAL) & 1) != 0;
    }

    // Test seam: inject the ToUnicodeEx behaviour and the initial caps state.
    internal KeyDecoder(ToUnicodeFn toUnicode, bool capsToggled = false)
    {
        _toUnicode = toUnicode;
        _capsToggled = capsToggled;
    }

    /// <summary>Current CapsLock toggle, exposed for tests.</summary>
    internal bool CapsToggled => _capsToggled;

    public Keystroke? Decode(KeyboardKeyEvent e)
    {
        // Modifier transitions only update state; they never produce output —
        // neither on press nor on release.
        if (TryUpdateModifier(e))
            return null;

        // Non-modifier key-ups carry nothing for the buffer.
        if (!e.IsKeyDown)
            return null;

        ushort vk = e.VirtualKey;
        double t = e.TimestampMs;

        // Caps is decoded as a modifier above only for its latch; the down
        // edge also flips the toggle. Handled in TryUpdateModifier.

        // Chords the application owns — we cannot predict their text effect,
        // and that includes the editing keys: Ctrl+Backspace deletes a whole
        // word on screen, so it must NOT decode as a plain Backspace (which
        // models one character — and, revert armed, would trigger an injection
        // under a physically held Ctrl). Win + anything is always a shortcut.
        // Ctrl is a shortcut UNLESS it is the Ctrl+Alt (AltGr) chord, which on
        // many layouts is a legitimate character composition and must be
        // decoded. Alt without Ctrl is a menu/accelerator chord, also a
        // shortcut.
        bool altGr = CtrlDown && AltDown;
        if (WinDown)
            return Keystroke.Of(KeystrokeKind.Shortcut, t);
        if (CtrlDown && !altGr)
            return Keystroke.Of(KeystrokeKind.Shortcut, t);
        if (AltDown && !CtrlDown)
            return Keystroke.Of(KeystrokeKind.Shortcut, t);

        // Editing / navigation keys win before any character translation.
        switch (vk)
        {
            case VK_BACK: return Keystroke.Of(KeystrokeKind.Backspace, t);
            case VK_RETURN: return Keystroke.Of(KeystrokeKind.Enter, t);
            case VK_TAB: return Keystroke.Of(KeystrokeKind.Tab, t);
            case VK_ESCAPE: return Keystroke.Of(KeystrokeKind.Escape, t);
            case VK_DELETE: return Keystroke.Of(KeystrokeKind.Delete, t);
            case VK_PRIOR or VK_NEXT or VK_END or VK_HOME
              or VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN:
                return Keystroke.Of(KeystrokeKind.Navigation, t);
        }

        // Otherwise resolve the character under the foreground layout.
        return Translate(vk, e.ScanCode, altGr, t);
    }

    private Keystroke Translate(ushort vk, ushort scanCode, bool altGr, double t)
    {
        byte[] state = BuildKeyState(altGr);
        var buffer = new StringBuilder(8);
        int code = _toUnicode(vk, scanCode, state, buffer);

        if (code > 0)
            return new Keystroke(KeystrokeKind.Text, buffer.ToString(0, Math.Min(code, buffer.Length)), t);
        if (code < 0)
            return Keystroke.Of(KeystrokeKind.DeadKey, t);
        return Keystroke.Of(KeystrokeKind.Other, t);
    }

    // 256-byte state array as ToUnicodeEx expects: high bit (0x80) = key down,
    // low bit (0x01) on VK_CAPITAL = toggle on. Only the keys that influence
    // character production are set. For an AltGr chord, Ctrl+Alt are marked
    // down so the layout's AltGr level is selected.
    private byte[] BuildKeyState(bool altGr)
    {
        var state = new byte[256];
        if (ShiftDown)
        {
            state[VK_SHIFT] = 0x80;
            state[VK_LSHIFT] = 0x80;
            state[VK_RSHIFT] = 0x80;
        }
        if (altGr)
        {
            state[VK_CONTROL] = 0x80;
            state[VK_LCONTROL] = 0x80;
            state[VK_MENU] = 0x80;
            state[VK_RMENU] = 0x80; // AltGr is physically the right Alt
        }
        if (_capsToggled)
            state[VK_CAPITAL] = 0x01;
        return state;
    }

    // Updates a modifier latch from a transition. Returns true when the event
    // WAS a modifier (handled, no output), false otherwise.
    private bool TryUpdateModifier(KeyboardKeyEvent e)
    {
        bool down = e.IsKeyDown;
        switch (e.VirtualKey)
        {
            case VK_LSHIFT: _lShift = down; return true;
            case VK_RSHIFT: _rShift = down; return true;
            case VK_SHIFT: _genShift = down; return true;
            case VK_LCONTROL: _lCtrl = down; return true;
            case VK_RCONTROL: _rCtrl = down; return true;
            case VK_CONTROL: _genCtrl = down; return true;
            case VK_LMENU: _lAlt = down; return true;
            case VK_RMENU: _rAlt = down; return true;
            case VK_MENU: _genAlt = down; return true;
            case VK_LWIN: _lWin = down; return true;
            case VK_RWIN: _rWin = down; return true;
            case VK_CAPITAL:
                if (down) _capsToggled = !_capsToggled; // toggle latches on the down edge
                return true;
            default:
                return false;
        }
    }

    // Real ToUnicodeEx against the foreground window's layout. The HKL is
    // resolved per call: the foreground app (hence its layout) can change
    // between keystrokes.
    private static int RealToUnicode(ushort vk, ushort scanCode, byte[] keyState, StringBuilder buffer)
    {
        uint threadId = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        IntPtr hkl = KeyboardStateInterop.GetKeyboardLayout(threadId);
        buffer.EnsureCapacity(8);
        buffer.Length = 0;
        return KeyboardStateInterop.ToUnicodeEx(
            vk, scanCode, keyState, buffer, buffer.Capacity, TOUNICODE_NOSTATECHANGE, hkl);
    }
}

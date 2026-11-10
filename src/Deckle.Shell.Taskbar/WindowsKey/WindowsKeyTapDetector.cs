using Deckle.Input;

namespace Deckle.Shell.Taskbar;

// Tells a Windows key pressed on its own from the Windows key used in a
// shortcut, from the key events alone. Pure: no Win32 call, no clock, no
// thread. The caller feeds every keyboard transition in order and carries out
// the action each one returns, so the sequences are tested without a keyboard.
//
// Windows opens Start when the Windows key comes up and no other key went down
// since it did. So the first action comes at the key-down: SuppressStart asks
// for a throwaway key press that makes the press count as a shortcut. If the
// key then comes up with nothing else pressed, that was a lone tap, and
// FocusTaskbar is the second action. Any other key going down while the
// Windows key is held makes it a shortcut, and the tracker steps back.
//
// Events the system injected (hDevice of zero in Raw Input) never count: that
// keeps the tracker from reacting to its own throwaway key and to the Windows
// + T it sends, and it leaves a remapper's or an on-screen keyboard's output to
// Windows alone.
public sealed class WindowsKeyTapDetector
{
    private const ushort VkLeftWindows = 0x5B;
    private const ushort VkRightWindows = 0x5C;

    private const int Left = 1;
    private const int Right = 2;

    private bool _enabled;

    // The Windows keys physically held, Left and Right as bits.
    private int _held;

    // Another key went down while a Windows key was held.
    private bool _shortcut;

    /// <summary>
    /// While false every event is ignored and nothing is remembered. Turning it
    /// off or on starts from a clean slate: a Windows key already down at that
    /// moment is not tracked, so Start still opens for it.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            _held = 0;
            _shortcut = false;
        }
    }

    public WindowsKeyAction OnKey(in KeyboardKeyEvent key)
    {
        if (!_enabled || key.IsInjected) return WindowsKeyAction.None;

        int bit = BitOf(key.VirtualKey);
        return bit == 0
            ? OnOtherKey(key.IsKeyDown)
            : key.IsKeyDown ? OnWindowsDown(bit) : OnWindowsUp(bit);
    }

    private static int BitOf(ushort virtualKey) => virtualKey switch
    {
        VkLeftWindows => Left,
        VkRightWindows => Right,
        _ => 0,
    };

    private WindowsKeyAction OnWindowsDown(int bit)
    {
        // Held and nothing else pressed: the keyboard repeating the key, which
        // the first press already answered.
        if ((_held & bit) != 0 && !_shortcut) return WindowsKeyAction.None;

        // A second Windows key joining the first one is the same press.
        if (_held != 0 && !_shortcut)
        {
            _held |= bit;
            return WindowsKeyAction.None;
        }

        // A new press. When another key went down during the last one, the
        // keyboard repeats that key and never the Windows key, so a Windows
        // key arriving now is a new press even if its release was never seen
        // (Win+L: the lock screen takes the release).
        _held = bit;
        _shortcut = false;
        return WindowsKeyAction.SuppressStart;
    }

    private WindowsKeyAction OnWindowsUp(int bit)
    {
        if ((_held & bit) == 0) return WindowsKeyAction.None;

        _held &= ~bit;
        if (_held != 0) return WindowsKeyAction.None;

        bool lone = !_shortcut;
        _shortcut = false;
        return lone ? WindowsKeyAction.FocusTaskbar : WindowsKeyAction.None;
    }

    private WindowsKeyAction OnOtherKey(bool isKeyDown)
    {
        if (isKeyDown && _held != 0) _shortcut = true;
        return WindowsKeyAction.None;
    }

    /// <summary>
    /// A mouse or touchpad button went down, or the wheel turned. With the
    /// Windows key held that is a shortcut too (Windows + click, Windows + wheel
    /// in some tools), so its release must not bring the taskbar forward.
    /// </summary>
    public void OnPointer()
    {
        if (_enabled && _held != 0) _shortcut = true;
    }
}

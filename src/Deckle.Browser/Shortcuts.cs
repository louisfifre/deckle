namespace Deckle.Browser;

/// <summary>What a shortcut asks the window to do.</summary>
public enum ShortcutCommand
{
    NewTab,
    CloseTab,
    ReopenClosedTab,
    FocusAddress,
    Find,
    Reload,
    Back,
    Forward,
    NextTab,
    PreviousTab,
    PrivateWindow,
}

/// <summary>A key with its modifiers. <see cref="Key"/> is the name of the key as the framework names a virtual key
/// ("T", "Tab", "Left", "F5"), so the domain holds no UI type.</summary>
public readonly record struct KeyGesture(string Key, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    public override string ToString() =>
        (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + Key;
}

// The browser's shortcuts as data: a gesture and the command it runs. Two
// commands never share a gesture (a test holds it), and one command may have
// several gestures (reload has F5 and Ctrl+R, the address field Ctrl+L and Alt+D).
public static class Shortcuts
{
    public static IReadOnlyList<(KeyGesture Gesture, ShortcutCommand Command)> All { get; } =
    [
        (new KeyGesture("T", Ctrl: true), ShortcutCommand.NewTab),
        (new KeyGesture("W", Ctrl: true), ShortcutCommand.CloseTab),
        (new KeyGesture("T", Ctrl: true, Shift: true), ShortcutCommand.ReopenClosedTab),
        (new KeyGesture("L", Ctrl: true), ShortcutCommand.FocusAddress),
        (new KeyGesture("D", Alt: true), ShortcutCommand.FocusAddress),
        (new KeyGesture("F", Ctrl: true), ShortcutCommand.Find),
        (new KeyGesture("F5"), ShortcutCommand.Reload),
        (new KeyGesture("R", Ctrl: true), ShortcutCommand.Reload),
        (new KeyGesture("Left", Alt: true), ShortcutCommand.Back),
        (new KeyGesture("Right", Alt: true), ShortcutCommand.Forward),
        (new KeyGesture("Tab", Ctrl: true), ShortcutCommand.NextTab),
        (new KeyGesture("Tab", Ctrl: true, Shift: true), ShortcutCommand.PreviousTab),
        (new KeyGesture("N", Ctrl: true, Shift: true), ShortcutCommand.PrivateWindow),
    ];

    /// <summary>The command a gesture runs, none when it is not a shortcut.</summary>
    public static ShortcutCommand? Find(KeyGesture gesture)
    {
        foreach ((KeyGesture g, ShortcutCommand command) in All)
            if (g == gesture) return command;
        return null;
    }
}

// The tabs closed in this window, most recent last, so Ctrl+Shift+T opens the
// last one again. Memory only, never saved, and small.
public sealed class ClosedTabs(int capacity = 20)
{
    private readonly List<(string Url, string Title)> _stack = [];

    public int Count => _stack.Count;

    /// <summary>A blank page is not worth reopening: nothing is kept for it.</summary>
    public void Push(string url, string title)
    {
        if (url.Length == 0 || url == "about:blank") return;
        _stack.Add((url, title));
        if (_stack.Count > capacity) _stack.RemoveAt(0);
    }

    /// <summary>The last closed tab, taken off the stack; null when none.</summary>
    public (string Url, string Title)? Pop()
    {
        if (_stack.Count == 0) return null;
        (string Url, string Title) last = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return last;
    }
}

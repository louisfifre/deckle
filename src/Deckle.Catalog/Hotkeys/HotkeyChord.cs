using System.Text.Json.Serialization;

namespace Deckle.Catalog;

// A global hotkey as the user binds it: modifiers plus one physical key.
//
// The key is stored as a keyboard scancode, not a virtual key. A scancode
// names a physical position, so a chord stays the same key when the keyboard
// layout changes (the key left of "1" is ` on QWERTY US and ² on AZERTY FR).
// The shell resolves the virtual key for the active layout at registration
// time and again on every layout change.
//
// Extended keys (arrows, Insert, Delete, Home, End, Page Up/Down, numpad
// Enter and /, right Ctrl/Alt) carry the 0xE0 prefix in the high byte:
// ScanCode = 0xE000 | sc. MapVirtualKeyExW accepts that form as is.
//
// Persisted through System.Text.Json as {"modifiers":8,"scanCode":41}.
public readonly record struct HotkeyChord(HotkeyModifiers Modifiers, uint ScanCode)
{
    // A chord can be registered only with at least one modifier and a key:
    // a bare key would swallow ordinary typing system-wide. Derived, so it is
    // kept out of the persisted form.
    [JsonIgnore]
    public bool IsComplete => Modifiers != HotkeyModifiers.None && ScanCode != 0;
}

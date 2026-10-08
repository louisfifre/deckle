using System;
using System.Collections.Generic;

namespace Deckle.Catalog;

// A chord as one line of text, "Ctrl + Shift + `": the modifiers in the order
// Windows itself writes them (Ctrl, Shift, Alt, Win), then the key name from
// the active layout. Used where a chord is read rather than edited, such as the
// capture button's accessible name. Modifier names stay the technical key
// names, the way the keycaps print them.
public static class HotkeyChordText
{
    // Null when the chord is unbound: the caller picks its own "not set" wording.
    public static string? Format(HotkeyChord? chord)
        => Format(chord, HotkeyKeyNames.Get);

    // keyName resolves a scancode to its display name; the overload exists so the
    // ordering can be checked without depending on the machine's layout.
    public static string? Format(HotkeyChord? chord, Func<uint, string> keyName)
    {
        if (chord is not { } c) return null;

        var parts = new List<string>(5);
        if (c.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (c.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (c.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (c.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        if (c.ScanCode != 0) parts.Add(keyName(c.ScanCode));

        return string.Join(" + ", parts);
    }
}

namespace Deckle.Catalog;

// Modifier keys of a global hotkey chord. The bit values are the Win32
// MOD_ALT / MOD_CONTROL / MOD_SHIFT / MOD_WIN flags of RegisterHotKey, so the
// shell turns a chord into its fsModifiers with a plain cast.
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

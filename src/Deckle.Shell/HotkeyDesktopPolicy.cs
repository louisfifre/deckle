namespace Deckle.Shell;

// Which input desktop the hotkeys follow. Pure: no Win32 call, no state.
//
// Windows takes a hotkey on the input desktop only, and the screen saver runs
// on a desktop of its own, "Screen-saver", the non-secure one included
// (measured on Windows 11 24H2; the Desktops page on Microsoft Learn says
// non-secure screen savers stay on Default). Held on Default alone, a chord
// pressed over the screen saver reached the app under it as a plain key.
//
// The secure screen saver ("On resume, display logon screen") is not
// followed: the person asked Windows to lock the machine behind it, and a
// chord that starts a recording would reach past that lock. The Winlogon
// desktop is never followed either, for the same reason.
public static class HotkeyDesktopPolicy
{
    public const string ScreenSaverDesktop = "Screen-saver";

    public static bool Follows(string inputDesktop, bool screenSaverSecure) =>
        !screenSaverSecure
        && string.Equals(inputDesktop, ScreenSaverDesktop, StringComparison.OrdinalIgnoreCase);
}

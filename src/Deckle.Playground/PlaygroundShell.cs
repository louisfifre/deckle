namespace Deckle.Playground;

// Lets Playground pages reach the host PlaygroundWindow without holding a
// reference to it, like SettingsHost in Deckle.Settings: one typed, named
// delegate per capability, wired by the window, never a string lookup.
public static class PlaygroundShell
{
    // Tags: "home", "hud", "ambient". Null while no window exists, so
    // callers null-check.
    public static Action<string>? NavigateTo { get; set; }
}

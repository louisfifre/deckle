namespace Deckle.Playground;

// Lets Playground pages reach the host PlaygroundWindow without holding a
// reference to it, like SettingsHost in Deckle.Settings: one typed, named
// delegate per capability, wired by the window, never a string lookup.
public static class PlaygroundShell
{
    // Tags: "home", "hud", "ambient", "segmentation", "correction". Null
    // while no window exists, so callers null-check.
    public static Action<string>? NavigateTo { get; set; }

    // The delayed-correction lab needs a typed, monotonic view of the owning
    // Window's activation lifecycle. Unknown is deliberately distinct from
    // inactive: a lease cannot arm until the Window has published one event.
    internal static Func<PlaygroundWindowActivation>? ReadWindowActivation { get; set; }

    internal static event Action<PlaygroundWindowActivation>? WindowActivationChanged;

    internal static void PublishWindowActivation(PlaygroundWindowActivation activation)
        => WindowActivationChanged?.Invoke(activation);
}

internal readonly record struct PlaygroundWindowActivation(
    bool IsKnown,
    bool IsActive,
    long Generation);

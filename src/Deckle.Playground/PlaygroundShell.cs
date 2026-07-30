namespace Deckle.Playground;

// Static delegate registry that lets Playground Pages reach back into the
// host PlaygroundWindow without holding a reference to it. Mirrors the
// SettingsHost pattern in Deckle.Settings : nominal delegates, one per
// capability, wired by PlaygroundWindow at construction. Pages call them
// directly (e.g. HomePage.OnHudCardClick → PlaygroundShell.NavigateTo?.Invoke("hud")).
//
// Not a Service Locator : each delegate is typed and named. Adding a hook
// is a deliberate code change here + on the calling side, not a string
// lookup.
public static class PlaygroundShell
{
    // Switches the PlaygroundWindow's NavigationView selection to the item
    // bearing the given tag ("home" / "hud" / "ambient" / "segmentation" /
    // "correction"). Wired by
    // PlaygroundWindow on construction ; null until the window is first
    // instantiated. Pages must null-check before invoking — a tag passed
    // before the window exists is a no-op.
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

namespace Deckle.Hud;

// Edge-triggered state shared by Deckle-authored animation drivers. Disabling
// lets a finite transition snap to its destination. Enabling deliberately
// raises nothing, so past transitions are never replayed.
internal sealed class AnimationGate
{
    public AnimationGate(bool isEnabled) => IsEnabled = isEnabled;

    public bool IsEnabled { get; private set; }

    public event Action? Disabled;

    public bool SetEnabled(bool isEnabled)
    {
        if (IsEnabled == isEnabled) return false;

        IsEnabled = isEnabled;
        if (!isEnabled)
            Disabled?.Invoke();

        return true;
    }
}

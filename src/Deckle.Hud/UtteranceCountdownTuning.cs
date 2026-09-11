using Deckle.Composition;

namespace Deckle.Hud;

// Countdown owns its mask and contour only. Its Transcribing material comes
// from HudMaterialTuning, shared with the ordinary HUD material controls.
public static class UtteranceCountdownTuning
{
    private static UtteranceCountdownConfig _current = new();
    public static UtteranceCountdownConfig Current => Volatile.Read(ref _current);
    public static event Action? Changed;

    public static void Apply(UtteranceCountdownConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config == Current) return;
        Volatile.Write(ref _current, config);
        Changed?.Invoke();
    }
}

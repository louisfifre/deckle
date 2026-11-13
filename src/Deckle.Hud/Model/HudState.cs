namespace Deckle.Hud;

// Public because HudChrono, a public control, takes it in ApplyState. Message is a
// HudWindow-level state (the chrono is hidden while a message shows); it lives
// here so the hosting state machine can pass a single enum to ApplyState
// without an extra mapping table.
public enum HudState
{
    Hidden,
    Charging,
    Recording,
    Transcribing,
    Rewriting,
    Message,
}

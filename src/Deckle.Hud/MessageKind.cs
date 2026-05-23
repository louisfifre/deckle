namespace Deckle.Hud;

// HudMessage payload types — the message kind enum and the data record
// HudWindow constructs when surfacing a transient banner. Internal because
// the consumers (HudWindow, HudOverlayWindow, HudPalette, HudMessage) all
// live in this assembly. The visual state enum used by HudChrono lives
// in HudState.cs (public, consumed cross-assembly by App + Playground).

internal enum MessageKind
{
    Success,
    Critical,
    Warning,
    Informational,
}

internal sealed record MessagePayload(
    MessageKind Kind,
    string Title,
    string Subtitle,
    TimeSpan Duration);

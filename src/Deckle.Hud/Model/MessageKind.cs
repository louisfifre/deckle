namespace Deckle.Hud;

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

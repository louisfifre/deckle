namespace Deckle.Composition;

public sealed record UtteranceCountdownConfig
{
    // Reach is the fraction of the HUD half-width lit along the top and
    // bottom edges, measured from the edge centre: 0 = edge centre, 1 = corner.
    public float StartReach { get; init; } = 0f;
    public float EndReach { get; init; } = 1f;
    public float StrokeThickness { get; init; } = 4f;
    public float EdgeFadeTurns { get; init; } = 0.01f;
    public float FadeCurve { get; init; } = 1f;
}

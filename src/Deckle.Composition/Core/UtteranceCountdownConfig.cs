namespace Deckle.Composition;

public sealed record UtteranceCountdownConfig
{
    public float StartDegrees { get; init; } = 12f;
    public float EndDegrees { get; init; } = -12f;
    public float StrokeThickness { get; init; } = 4f;
    public float LeadFadeTurns { get; init; } = 0.01f;
    public float TailFadeTurns { get; init; } = 0.01f;
    public float FadeCurve { get; init; } = 1f;
}

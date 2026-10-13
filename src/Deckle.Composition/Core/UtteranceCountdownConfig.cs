namespace Deckle.Composition;

// The gauge's own two knobs. Everything else about its look — the fade of its
// two wedges, whether the cone spins, the greyscale grading — is the Recording
// outline's, read from the Recording* fields of ConicArcStrokeConfig: the gauge
// is that outline opened by the pause, so the two are tuned with one gesture
// and cannot drift apart.
public sealed record UtteranceCountdownConfig
{
    // Reach is the fraction of the HUD half-width lit along the top and
    // bottom edges, measured from the edge centre: 0 = edge centre, 1 = corner.
    public float StartReach { get; init; } = 0f;
    public float EndReach { get; init; } = 1f;
}

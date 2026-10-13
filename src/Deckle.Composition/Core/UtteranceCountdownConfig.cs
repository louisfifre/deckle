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
    //
    // Below 0 the gauge stays closed until the lerped reach crosses zero: the
    // first part of every pause — the short dips between words that reset
    // before they mean anything — is hidden without a timer, and the opening
    // still reaches full at the true cut, only later and faster. Above 1 the
    // wedges pass the corners before the cut, so the last instants no longer
    // read as a sudden full ring. −0.15 / 1.75 were set on screen on
    // 2026-10-13: a first pass at −0.4 / 1.4 "came from too far", so the gauge
    // now opens after 8 % of the pause instead of 22 % and passes the corners
    // at 60 % instead of 78 %, running well beyond them by the cut.
    public float StartReach { get; init; } = -0.15f;
    public float EndReach { get; init; } = 1.75f;
}

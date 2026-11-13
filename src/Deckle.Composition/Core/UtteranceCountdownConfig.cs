namespace Deckle.Composition;

// The gauge's own knobs: where its light runs, how it runs there, and how it
// moves. Everything else about its look — the fade of its two wedges, whether
// the cone spins, the material — is the Recording outline's, read from the
// Recording* fields of ConicArcStrokeConfig: the gauge is that outline opened
// by the pause, so the two are tuned with one gesture and cannot drift apart.
public sealed record UtteranceCountdownConfig
{
    // Reach is the fraction of the HUD half-width lit along the top and
    // bottom edges, measured from the edge centre: 0 = edge centre, 1 = corner.
    //
    // Below 0 the gauge stays closed until the lerped reach crosses zero.
    // Above 1 the wedges pass the corners before the cut. The defaults are
    // the values Louis tuned last and kept (2026-11-13): a start below zero,
    // so the short dips between words leave the gauge closed, and an end at
    // the corners, so the full ring arrives with the cut itself.
    public float StartReach { get; init; } = -0.25f;
    public float EndReach { get; init; } = 1f;

    // The course: how the segmenter's linear progress through the pause maps
    // to the reach, as a CSS cubic-bezier on the unit square. The defaults
    // ease in: almost still through the first half of the pause, then faster
    // and faster up to the cut. (0, 0, 1, 1) is the plain lerp.
    public float CourseEaseP1X { get; init; } = 0.66f;
    public float CourseEaseP1Y { get; init; } = 0f;
    public float CourseEaseP2X { get; init; } = 1f;
    public float CourseEaseP2Y { get; init; } = 0.33f;

    // The gauge's motions. Following a capture sample, and reaching full at a
    // cut, take FollowSeconds, linearly: the next sample restarts the move
    // from the point reached, so the gauge glides that long behind the
    // capture with a continuous velocity.
    //
    // Going back to rest is one move, whatever sends the gauge there: speech
    // resuming during the pause, or a cut whose completion hold has shown the
    // full gauge. It takes RetreatSeconds on the Retreat ease, which starts
    // fast and arrives slowly: the "fast out, slow in" curve Windows documents
    // for an object returning to rest. Louis asked for one return, not two
    // (2026-10-22); the cut once had its own, shorter and accelerating. The
    // defaults are the 0.33 s he settled on and that curve, (0, 0, 0, 1).
    // FollowSeconds is his 0.1 s: the gauge trails the capture closely.
    public double FollowSeconds { get; init; } = 0.1;
    public double RetreatSeconds { get; init; } = 0.33;
    public float RetreatEaseP1X { get; init; } = 0f;
    public float RetreatEaseP1Y { get; init; } = 0f;
    public float RetreatEaseP2X { get; init; } = 0f;
    public float RetreatEaseP2Y { get; init; } = 1f;
}

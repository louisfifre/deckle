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
    // Above 1 the wedges pass the corners before the cut, so the last instants
    // no longer read as a sudden full ring. The start was −0.15 from
    // 2026-10-13 to hide the short dips between words; on 2026-10-15 Louis
    // asked for a start at zero with the course below doing the hiding: a
    // slow start keeps the first part of every pause near the centre, and
    // the opening still reaches full at the true cut.
    public float StartReach { get; init; } = 0f;
    public float EndReach { get; init; } = 1.75f;

    // The course: how the segmenter's linear progress through the pause maps
    // to the reach, as a CSS cubic-bezier on the unit square. The defaults
    // start slow, run fast through the middle and brake only at the very end
    // (Louis, 2026-10-15: "commencer à zéro et accélérer doucement jusqu'à
    // une vitesse assez forte et ralentir juste à la toute fin").
    // (0, 0, 1, 1) is the plain lerp the gauge had before.
    public float CourseEaseP1X { get; init; } = 0.55f;
    public float CourseEaseP1Y { get; init; } = 0f;
    public float CourseEaseP2X { get; init; } = 0.85f;
    public float CourseEaseP2Y { get; init; } = 1f;

    // The gauge's motions. Following a capture sample, and reaching full at a
    // cut, take FollowSeconds, linearly: the next sample restarts the move
    // from the point reached, so the gauge glides that long behind the
    // capture with a continuous velocity. Speech resuming is one move back
    // to rest over RetreatSeconds, on its own curve; the defaults are the
    // Windows "fast" duration and the "fast out, slow in" curve Windows
    // documents for an object quickly returning to rest.
    public double FollowSeconds { get; init; } = 0.167;
    public double RetreatSeconds { get; init; } = 0.167;
    public float RetreatEaseP1X { get; init; } = 0f;
    public float RetreatEaseP1Y { get; init; } = 0f;
    public float RetreatEaseP2X { get; init; } = 0f;
    public float RetreatEaseP2Y { get; init; } = 1f;

    // The cut's release: the move back to rest once the completion hold has
    // shown the full gauge. The retreat's opposite, short and accelerating,
    // the gesture of coming back to talking (Louis, 2026-10-15: "le geste
    // arrière en accélérant, mais très très vite cette fois"). An ease-in
    // where the retreat is an ease-out, over a little more than half the
    // retreat's time.
    public double CutSeconds { get; init; } = 0.1;
    public float CutEaseP1X { get; init; } = 0.7f;
    public float CutEaseP1Y { get; init; } = 0f;
    public float CutEaseP2X { get; init; } = 1f;
    public float CutEaseP2Y { get; init; } = 1f;
}

// Which of the two moves back to rest a SetProgress(0) plays: speech
// resuming retreats, a cut's hold ending releases. The renderer tells them
// apart from the capture's completed count, never from the value alone.
public enum UtteranceCountdownRelease
{
    Retreat,
    Cut,
}

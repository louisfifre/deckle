using System.Numerics;

namespace Deckle.Composition;

// ── SwipeWaveAnimator ─────────────────────────────────────────────────────────
//
// Reusable left→right reveal animator for a row of N elements (digits, glyphs,
// characters). Each MARKED element gets its own opacity ENVELOPE — a linear
// fade-in, a hold at full, a linear fade-out — and the envelopes are LAUNCHED
// one after another, left→right, staggered in time. Because an envelope lasts
// longer than the gap between launches, several elements are lit at once: that
// overlap IS the swipe. Each lit element reaches FULL opacity (heat = 1) during
// its hold, so the revealed material reads as vividly as its source — never the
// washed-out "one bright element dragging a dim tail" of a comet.
//
// Two roles, two curves — kept distinct on purpose:
//   - The ENVELOPE shapes a SINGLE element's fade. It is LINEAR (a trapezoid,
//     or a triangle at the extreme), never eased: an element appears and leaves
//     at a constant rate, and dwells at full in between.
//   - The EASE shapes the CADENCE — how the launches FOLLOW one another. The
//     k-th marked element launches at `span · ease(k/(M-1))`, so an ease-in-out
//     bunches the launches toward the ends of the sweep and spreads them through
//     the middle, an ease-out front-loads them, and so on. The ease never
//     touches a single element's fade; it only redistributes the launch instants.
//
// Per-element state, two parallel arrays:
//   - `Changed[i]` — caller-maintained: true ⇒ this element is lit by the wave
//     and joins the launch order. Unmarked elements stay pinned at heat 0.
//   - `Heat[i]`    — animator-maintained: the element's current envelope value
//     in [0, 1]. Recomputed from scratch every Tick — there is NO inter-frame
//     inertia, so a live tunable change shows on the very next frame (unlike the
//     old comet, whose stateful rise/decay lerp swallowed slider tweaks).
//
// Extracted from Controls/HudChrono.xaml.cs on 2026-05-02; rewritten from a
// single-head comet (rise-fast / decay-slow heat lerp) to this per-element
// envelope + eased-stagger model on 2026-06-14 — the comet only ever lit one
// element brightly, which read as "one digit at a time" instead of a wave.
// Future reuse target: Ask-Ollama text reveal — same algorithm over a row of
// glyphs unveiled left→right as the model streams.
//
// ── Tunables ──────────────────────────────────────────────────────────────────
// `public static` (not const / readonly) so HudPlayground can tune the cadence
// and envelope live. Process-wide — every instance reads the same values, and
// Tick reads them fresh each frame, so a slider drag lands on the next vsync.
public sealed class SwipeWaveAnimator
{
    // Default element count — kept at 6 to mirror the HudChrono digit row
    // (Min1 Min2 Sec1 Sec2 Cs1 Cs2). Other consumers pass their own count.
    public const int DefaultElementCount = 6;

    // SwipeCycleSeconds — full period of one sweep, INCLUDING the rest beat
    // before it repeats. The wave's active span (last launch + one envelope) is
    // CAPPED to fit inside this: launches spread over at most cycle − envelope,
    // so the whole sweep always lands inside one period and the remainder is the
    // pause between sweeps. Nothing can overflow into the next sweep.
    public static float SwipeCycleSeconds = 2.4f;

    // SwipeStaggerSeconds — gap between two consecutive launches (left→right).
    // The BASE cadence; the ease redistributes launches around it. Smaller ⇒
    // launches closer ⇒ more elements lit at once. The total spread it implies,
    // stagger · (M−1), is CAPPED at cycle − envelope: past that the stagger
    // saturates rather than pushing pulses past the cycle seam (which would make
    // the digits collide instead of follow one another). This is the cap that
    // keeps the stagger in step with the cycle.
    public static float SwipeStaggerSeconds = 0.1f;

    // SwipeEnvelopeSeconds — how long ONE element stays inside its
    // fade-in / hold / fade-out envelope. The count of simultaneously-lit
    // elements is ≈ SwipeEnvelopeSeconds / SwipeStaggerSeconds; at the defaults
    // the envelope (1.4 s) far outlasts the total launch spread (stagger · (M−1) ≈
    // 0.5 s over six digits), so the whole row lights almost together with only a
    // short stagger at the leading edge — a soft synchronous pulse rather than a
    // narrow travelling band.
    public static float SwipeEnvelopeSeconds = 1.4f;

    // SwipeRampFraction — the linear fade-in (and equal fade-out) portion of the
    // envelope, as a fraction of its length. The middle 1 − 2·ramp holds at FULL
    // opacity, which is what makes each digit reach the vivid conic instead of
    // looking washed out. 0.5 ⇒ a pure triangle (peak only, no hold); → 0 ⇒ a
    // hard on/off box. Clamped to [0, 0.5].
    public static float SwipeRampFraction = 0.4f;

    // SwipeEaseP1/P2 — cubic-bezier control points for the launch CADENCE (NOT
    // the per-element fade, which stays linear). Defaults form a symmetric
    // ease-in-out: launches bunch slightly at the start and end of the sweep and
    // spread through the middle. A linear cadence (even spacing) is P1=(⅓,⅓),
    // P2=(⅔,⅔).
    public static Vector2 SwipeEaseP1 = new(0.4f, 0f);
    public static Vector2 SwipeEaseP2 = new(0.6f, 1f);

    private readonly bool[] _changed;
    private readonly float[] _heat;

    // Element count is fixed at construction — _changed and _heat are sized once
    // and the per-frame loop never re-allocates.
    public int ElementCount => _heat.Length;

    public SwipeWaveAnimator(int elementCount = DefaultElementCount)
    {
        if (elementCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(elementCount),
                "Element count must be positive.");
        _changed = new bool[elementCount];
        _heat    = new float[elementCount];
    }

    // ── Changed flags (caller-maintained) ────────────────────────────────────
    // A marked element joins the left→right launch order and is lit by the
    // wave. Caller flips these from its own state machine (Recording: each digit
    // flip flags itself; Ask-Ollama future: each newly-revealed glyph flags
    // itself). Unmarked elements never receive heat.

    public bool IsChanged(int index) => _changed[index];

    public void SetChanged(int index, bool value) => _changed[index] = value;

    public void ClearAllChanged()
    {
        for (int i = 0; i < _changed.Length; i++) _changed[i] = false;
    }

    // ── Heat (animator-maintained, caller-readable) ─────────────────────────
    // Heat is read every frame by the caller to drive its own visual (sprite
    // Opacity, brush alpha, …). The animator owns the values; SetHeat /
    // ClearAllHeat let the caller seed or reset without going through Tick —
    // used for the Recording-time flash, where the caller writes heat = 1 on the
    // freshly-changed element while Tick is dormant. Once the swipe runs, Tick
    // recomputes every heat from the envelope and the seed is superseded.

    public float GetHeat(int index) => _heat[index];

    public void SetHeat(int index, float value) => _heat[index] = value;

    public void ClearAllHeat()
    {
        for (int i = 0; i < _heat.Length; i++) _heat[i] = 0f;
    }

    // ── Per-frame advance ────────────────────────────────────────────────────
    // Recompute every element's heat for the given elapsed time. Marked elements
    // are walked left→right; each is assigned a launch instant on the sweep, and
    // its heat is the envelope sampled at the time since that launch. Unmarked
    // elements are pinned to 0.
    //
    //   M        = number of marked elements
    //   tCycle   = elapsed wrapped into [0, SwipeCycleSeconds)
    //   spread   = min(SwipeStaggerSeconds · (M − 1), cycle − D)   (0 when M = 1)
    //   launch_k = max(launch_{k−1}, spread · clamp01(CubicBezier(k/(M−1))))
    //   rel      = tCycle − launch_k
    //   heat     = (0 ≤ rel ≤ D) ? Envelope(rel / D) : 0           (D = envelope)
    //
    // Two guards keep ANY tunable combination sane:
    //   - spread is capped at cycle − D so the whole sweep fits one period — no
    //     pulse wraps past the seam to collide with the next sweep's head.
    //   - launches are forced non-decreasing and the ease is clamped to [0, 1],
    //     so the digits always fire left→right however the cadence bezier is
    //     shaped (a control-point Y below 0 / above 1 can no longer reverse them).
    public void Tick(double elapsedSeconds)
    {
        int n = _heat.Length;

        // Count marked elements. Nothing marked ⇒ the whole row stays dark.
        int m = 0;
        for (int i = 0; i < n; i++) if (_changed[i]) m++;
        if (m == 0)
        {
            for (int i = 0; i < n; i++) _heat[i] = 0f;
            return;
        }

        float cycle    = SwipeCycleSeconds    > 0f ? SwipeCycleSeconds : 1f;
        float stagger  = MathF.Max(0f, SwipeStaggerSeconds);
        float envelope = MathF.Max(1e-4f, SwipeEnvelopeSeconds);

        // Cap the launch spread so the last launch + one envelope still fits the
        // cycle. Past the cap the stagger saturates instead of pushing pulses
        // over the seam (which made the digits collide rather than follow).
        float maxSpread = MathF.Max(0f, cycle - envelope);
        float spread    = MathF.Min(stagger * (m - 1), maxSpread);

        // Elapsed within the current sweep, wrapped into [0, cycle). The modulo
        // gives the looping for free — no keyframe roll-over to manage.
        float tCycle = (float)(elapsedSeconds % cycle);
        if (tCycle < 0f) tCycle += cycle;

        int k = 0;             // launch order among the marked elements, left→right
        float prevLaunch = 0f; // launches kept non-decreasing — see below
        for (int i = 0; i < n; i++)
        {
            if (!_changed[i]) { _heat[i] = 0f; continue; }

            // Launch instant: the cadence ease maps this element's normalised
            // order to a point on the spread. Clamp the eased value to [0, 1]
            // (a bezier whose control-point Y leaves [0, 1] overshoots) and take
            // the running max so the sequence only ever moves forward — the
            // digits never launch out of order. M == 1 ⇒ a single launch at 0.
            float orderFrac = (m == 1) ? 0f : (float)k / (m - 1);
            float eased     = Math.Clamp(
                Easing.CubicBezier(orderFrac, SwipeEaseP1, SwipeEaseP2), 0f, 1f);
            float launch    = MathF.Max(prevLaunch, spread * eased);
            prevLaunch = launch;

            // Time since this element's launch. The spread cap guarantees the
            // pulse [launch, launch + envelope] fits inside the cycle, so a digit
            // is simply dark before its launch and after its envelope — no
            // cross-seam wrap to manage.
            float rel = tCycle - launch;
            _heat[i] = (rel >= 0f && rel <= envelope) ? Envelope(rel / envelope) : 0f;
            k++;
        }
    }

    // Linear trapezoid envelope on u ∈ [0, 1]: rise 0→1 over [0, r], hold at 1
    // over [r, 1−r], fall 1→0 over [1−r, 1]. r = SwipeRampFraction clamped to
    // [0, 0.5]. r = 0.5 collapses the hold to a single point (triangle); r → 0
    // is a box (instant on, full for the whole width, instant off).
    private static float Envelope(float u)
    {
        float r = Math.Clamp(SwipeRampFraction, 0f, 0.5f);
        if (r <= 0f)     return 1f;              // box — full across the width
        if (u < r)       return u / r;           // linear rise
        if (u > 1f - r)  return (1f - u) / r;    // linear fall
        return 1f;                               // hold at full
    }
}

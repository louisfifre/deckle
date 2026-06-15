using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Windows.UI;

namespace Deckle.Composition;

public static partial class HudComposition
{
    // ╔════════════════════════════════════════════════════════════════════╗
    // ║  Processing stroke — single visual, live-modulated variants        ║
    // ╚════════════════════════════════════════════════════════════════════╝
    // One stroke is created on HUD state entry and kept alive across
    // Transcribing ↔ Rewriting. Per-state differentiation runs LIVE on
    // the same visual via Composition effects (SaturationEffect,
    // HueRotationEffect, ExposureEffect on the colour pipeline + Opacity
    // on the visual). ProcessingStroke.ApplyVariant blends toward the
    // target values over BlendSeconds — no surface rebuild, no GC, no lag.
    //
    // Recording uses the same stroke type but with a frozen-rotation
    // pipeline and paint-time geometry overrides (arc lobes parked at
    // visual 12/6 o'clock). Because paint-time knobs differ from the
    // rotating variant, crossing the Recording ↔ (Transcribing /
    // Rewriting) boundary requires a rebuild — HudChrono tears down and
    // recreates the stroke at that boundary. Transcribing ↔ Rewriting
    // stays live-blended on the same visual.
    //
    // All knobs live on ConicArcStrokeConfig below, split into blocks:
    //   1. "Baseline palette / rotations" — paint-time config, applies to
    //      all variants unless overridden (OklchLightness, HueRange,
    //      ConicSpan, ArcPeriod…).
    //   2. Runtime variants (Rewriting* / Transcribing* / Recording*) —
    //      the live knobs animated by ApplyVariant. Edit these to shape
    //      each state's look.
    //   3. Recording paint-time overrides (RecordingConic* /
    //      RecordingArcPhaseTurns / RecordingArcMirror) — consumed by
    //      CreateRecordingStroke to carve the frozen-rotation silhouette.
    //   4. BlendSeconds — per-variant transition duration.

    // ── Lexicon — vocabulary shared across the struct fields below ──────
    //
    // Paint-time (OKLCh conic palette baked once into a surface).
    // OKLCh is a perceptually uniform cylindrical colour space: at
    // constant L and C, all hues have the same perceived lightness
    // and saturation. HSV — which this used to use — does not: a
    // full-saturation HSV rainbow reads with yellow much brighter
    // than blue, which was visible as a top/bottom luminance
    // asymmetry on the conic wheel. OKLCh removes that asymmetry.
    //   OklchLightness 0 = black, 1 = white. 0.75 is bright-but-not-
    //                  blinding, comparable to a vivid mid-tone.
    //   OklchChroma    saturation in OKLab space. 0 = greyscale,
    //                  ~0.15 = vivid pastel, ~0.22 = near-maximum
    //                  in-gamut for most hues at L=0.75 (yellows and
    //                  blues start to clip above ~0.18). Gamut-clipped
    //                  values are clamped to [0, 1] sRGB at the end
    //                  — clipping reads as a gentle flattening of
    //                  those hues rather than a hard stop.
    //   HueStart       rotates hue 0 on the wheel (0 = red at 3 o'clock).
    //   HueRange       wheel slice. 1 = full rainbow, 0.5 = half, 0 = mono.
    //   WedgeCount     pie wedges. 360 = smooth ring, 12/24 = retro steps.
    //
    // Arc mask shape (white pie slice composited with the conic via
    // AlphaMaskEffect, with alpha ramps at both ends):
    //   Span           arc length in turns. 0.5 mirrored = half-circle
    //                  each; smaller = more "off" space between.
    //   LeadFade/Tail  fade extents in turns at each end (head fade-in /
    //                  tail fade-out). If Lead+Tail > Span they scale to
    //                  meet at the arc mid (pure bell, no flat core).
    //   FadeCurve      pow(t, curve) shape. 1 = linear ribbon,
    //                  2 = quadratic soft fade, 3+ = crisp comet bell,
    //                  <1 = near hard-edged solid.
    //   Mirror         paint a second arc at +π for a symmetric double
    //                  comet (Span clamps to 0.5 in mirror mode).
    //
    // Rotation (applied independently to the conic and the arc mask —
    // rational period ratios like 2:1 or 3:2 close cleanly every LCM):
    //   PeriodSeconds  seconds per full turn. Lower = faster.
    //   Direction      +1 CW, -1 CCW.
    //   PhaseTurns     start offset in turns (0..1).
    //   EaseP1/P2        cubic-bezier control points. (0,0,1,1) = linear,
    //                    (0.42,0,0.58,1) = standard ease-in-out, sharper
    //                    curves give bigger speed contrast.
    //   MinSpeedFraction fraction of the mean angular velocity
    //                    (ω_mean = 2π/period) guaranteed at every instant
    //                    of the cycle. 0 = pure easing (may visibly freeze
    //                    on the bezier plateaus). 0.3 = never below 30% of
    //                    mean. 1 = strictly constant rotation at the mean
    //                    (no pulsation). Raising the floor compresses the
    //                    peak in the same stroke — the eye reads it as
    //                    "calmer, more continuous", not "faster". See
    //                    StartRotation header for the closed-form
    //                    ω_min / ω_max expressions.
    //
    // Runtime variant knobs (live properties on the SINGLE kept-alive
    // stroke — SaturationEffect, HueRotationEffect, ExposureEffect on the
    // colour pipeline, plus SpriteVisual.Opacity — animated by
    // ApplyVariant. Switching variants is a property animation on the
    // same GPU resources — no surface rebuild, no GC, no lag):
    //   Saturation     multiplier on the baked conic. 0 = greyscale,
    //                  1 = baseline colour. Combines with OklchChroma.
    //   HueShiftTurns  runtime rotation of the colour wheel.
    //                  0 = no shift, 0.5 = red↔cyan swap, 1 = no change.
    //                  Negatives shift the other way.
    //   Exposure       EV stops. 0 = no change, +1 ≈ 2× brighter,
    //                  -1 ≈ half. Typical range [-2, +2]. Split
    //                  Dark / Light for Transcribing so the greyscale
    //                  stays readable against both substrates.
    //   Opacity        SpriteVisual.Opacity in [0..1]. Dims the whole
    //                  stroke including the silhouette; 0.6-0.8 reads
    //                  as a subtle calm variant.
    //   BlendSeconds   duration of the blend from the previous variant.
    //                  0.2-0.4 = snappy, 0.6-1.0 = breathing. Per-variant
    //                  so entering Transcribing can be slower than the
    //                  return to Rewriting.
    //
    // No base stroke layer — the permanent HUD outline is the DWM frame
    // (DWMWA_BORDER_COLOR = DWMWA_COLOR_DEFAULT in HudWindow), 1-dip and
    // theme/accent-aware. Rotating arcs composite on top; transparent
    // regions between arcs expose the DWM stroke.

    // Config for CreateConicArcStroke. Init-only fields with defaults —
    // each wrapper overrides only what it needs. The explicit
    // parameterless constructor is required by C# for struct field
    // initialisers to run on `new ConicArcStrokeConfig { ... }`.
    //
    // `internal` (not `private`) so the internal ProcessingStroke ctor
    // can reference it without CS0051. Still effectively HudComposition-
    // scoped — nothing outside this file constructs one.
    //
    // See the Lexicon above for what each field means; only per-field
    // deviations from the generic definition are repeated here.
    public readonly struct ConicArcStrokeConfig
    {
        public ConicArcStrokeConfig() {}

        // ── Colour palette (paint-time, baked once) ──────────────────────
        // OKLCh replaces HSV so the baked conic wheel has perceptually
        // uniform luminance across hues — critical for the
        // Saturation=0 greyscale variants, which otherwise inherit
        // HSV's top/bottom brightness asymmetry as a grey gradient.
        public float  OklchLightness     { get; init; } = 0.75f;
        public float  OklchChroma        { get; init; } = 0.3f;
        public float  HueStart           { get; init; } = 0f;
        public float  HueRange           { get; init; } = 1f;
        public int    WedgeCount         { get; init; } = 360;

        // ── Hue rotation — spins the conic under the fixed arc mask,
        //    so the colour at the arc head walks the wheel over time ─────
        public double HuePeriodSeconds   { get; init; } = 14.0;
        public float  HueDirection       { get; init; } = 1f;
        public float  HuePhaseTurns      { get; init; } = 0f;
        // Out-in shape at (0.125, 0.375) / (0.875, 0.625) — tangent at
        // the endpoints has slope 0.375 / 0.125 = 3.0 at both t=0 and
        // t=1. Same slope on both sides means the loop is C¹ across the
        // cycle seam: no "freeze at the plateau" reading between
        // iterations. The midsection dips below the mean (slow-down ≈
        // 0.5× mean around t=0.5) and the endpoints push above (pulse
        // ≈ 3× mean), but continuously — no pause on any frame.
        // Replaces the classic in-out (0.2, 0, 0.8, 1) whose zero-slope
        // endpoints forced MinSpeedFraction as a workaround.
        public float  HueEaseP1X         { get; init; } = 0.125f;
        public float  HueEaseP1Y         { get; init; } = 0.375f;
        public float  HueEaseP2X         { get; init; } = 0.875f;
        public float  HueEaseP2Y         { get; init; } = 0.625f;
        // Vestigial — the out-in curve above no longer plateaus at cycle
        // boundaries, so the linear blend is redundant. Kept at 0 so the
        // playground can still experiment with exotic ease curves that
        // DO need a floor, but untouched in the shipping path.
        public float  HueMinSpeedFraction { get; init; } = 0f;

        // ── Arc mask shape (white pie slice, alpha-ramped at both ends) ─
        public float  ConicSpanTurns     { get; init; } = 0.5f;
        public float  ConicLeadFadeTurns { get; init; } = 1f;
        public float  ConicTailFadeTurns { get; init; } = 1f;
        public float  ConicFadeCurve     { get; init; } = 4f;
        public bool   ArcMirror          { get; init; } = true;

        // ── Arc rotation — rotates the arc mask independently of the
        //    hue rotation. This is what the eye reads as "the speed of
        //    the loading animation" ─────────────────────────────────────
        public double ArcPeriodSeconds   { get; init; } = 8.0;
        public float  ArcDirection       { get; init; } = 1f;
        public float  ArcPhaseTurns      { get; init; } = 0f;
        // Same out-in shape as HueEase for the same reason — seam
        // continuity across the cycle loop without a plateau floor.
        public float  ArcEaseP1X         { get; init; } = 0.125f;
        public float  ArcEaseP1Y         { get; init; } = 0.375f;
        public float  ArcEaseP2X         { get; init; } = 0.875f;
        public float  ArcEaseP2Y         { get; init; } = 0.625f;
        public float  ArcMinSpeedFraction { get; init; } = 0f;

        // ── Clone-cone placement (the swipe's digit reveal) ─────────────
        // Where the reveal cone's centre sits within the host frame, as a
        // FRACTION of host size (DPI/size-independent, so one slider drives
        // both the live reveal and the ConicClone preview through the same
        // value). (0.5, 0.5) = centred — reproduces the contour's cone, so
        // each digit samples the same slice the contour shows at that point.
        // (0, 0) = top-left corner, the cone radiating from there.
        //
        // The reveal cone is a CLONE of the contour's: same OKLCh palette and
        // same breathing rotation (it shares the stroke's HueRotationProps and
        // EffectProps), but its OWN surface, auto-scaled so its inscribed
        // circle reaches every host corner FROM this centre. That guarantees a
        // digit anywhere in the row samples a painted pixel instead of falling
        // off the surface (out-of-bounds = transparent) — the coverage the
        // earlier shared-surface route lacked. The cone is a pure angular
        // gradient, invariant under radial scaling, so growing the surface only
        // extends coverage, never distorts the look.
        public float  CloneCentreXFraction { get; init; } = 196f / 272f; // 196 px on the 272-wide row
        public float  CloneCentreYFraction { get; init; } = 0f;          // apex at the row's top edge

        // ── Clone-cone palette — the reveal's OWN OKLCh lightness/chroma, decoupled
        //    from the contour's (OklchLightness/Chroma) so the swept digits can be
        //    pushed brighter / more chromatic for "peps" without touching the
        //    contour. This is the peps knob: ExposureEffect caps at +2 EV — too low
        //    for the lift the grey-on-Tertiary sweep needs — so the lift lives in the
        //    baked surface instead. Lightness above the contour's 0.75 reads as a
        //    brighter sweep in Transcribing (greyscale); chroma feeds the Rewriting
        //    colour. Painted into the clone surface by CloneSurfaceConfig. ─────────
        public float  CloneOklchLightness   { get; init; } = 0.9f;
        public float  CloneOklchChroma      { get; init; } = 0.3f;

        // ── Clone-cone rotation — INDEPENDENT from the contour's, so the
        //    reveal cone can spin at its own pace (a distinct animation, not
        //    a window locked to the contour). Only the speeds + directions
        //    split: the arc SHAPE (ConicSpan/Fade/Mirror), the ease curves,
        //    the phase and the palette stay shared, so a fresh clone looks
        //    identical to the contour until you tune the speeds apart. The
        //    reveal is a DOUBLE-comet (conic ⊗ arc mask) like the visible
        //    contour, NOT the naked cone — critical in Transcribing, where the
        //    greyscale (luminance-uniform OKLCh) cone shows no motion on its
        //    own and only the swept comet SHAPE reads. ─────────────────────
        public double CloneHuePeriodSeconds { get; init; } = 7.0;
        public float  CloneHueDirection     { get; init; } = -1f;
        public double CloneArcPeriodSeconds { get; init; } = 4.0;
        public float  CloneArcDirection     { get; init; } = -1f;

        // ── Rewriting variant — target values for the live effect
        //    pipeline. Baseline neutrals leave the baked palette alone ──
        public float  RewritingSaturation       { get; init; } = 1f;
        public float  RewritingHueShiftTurns    { get; init; } = 0f;
        public float  RewritingExposure         { get; init; } = 0f;
        public float  RewritingOpacity          { get; init; } = 1f;
        public double RewritingBlendSeconds     { get; init; } = 2;

        // ── Transcribing variant — greyscale (Saturation 0) by default.
        //    Saturation + Exposure are split Dark/Light because even with
        //    OKLCh's uniform-luminance baseline, the greyscale target
        //    still depends on the substrate (light on dark / dark on
        //    light) — exposure biases the baked L=0.75 neutral up or
        //    down to read against each theme. HueShift/Opacity stay
        //    unified — widen later if per-theme control is needed ───────
        public float  TranscribingSaturationDark  { get; init; } = 0f;
        public float  TranscribingSaturationLight { get; init; } = 0f;
        public float  TranscribingHueShiftTurns   { get; init; } = 0f;
        public float  TranscribingExposureDark    { get; init; } = 0.7f;
        public float  TranscribingExposureLight   { get; init; } = -1.2f;
        public float  TranscribingOpacity         { get; init; } = 1f;
        public double TranscribingBlendSeconds    { get; init; } = 2;

        // ── Recording variant — frozen-rotation stroke with RMS-driven
        //    opacity. Two blocks:
        //
        //    PAINT-TIME (baked into Win2D conic/arc surfaces when
        //    CreateRecordingStroke runs — cannot be animated live; edit
        //    the defaults and rebuild to iterate):
        //      - ConicSpan / LeadFade / TailFade / FadeCurve / ArcMirror
        //        shape the silhouette's arc geometry. Span 0.5 + Mirror
        //        covers the full perimeter as two 180° lobes. Fades
        //        auto-scale to bell if Lead+Tail > Span.
        //      - ArcPhaseTurns rotates the arc mask at paint-time to
        //        park the lobes at visual 12/6 o'clock (see
        //        CreateRecordingStroke header for the phase math).
        //
        //    RUNTIME (consumed by ApplyVariant — animated via the same
        //    live effect pipeline as Transcribing/Rewriting, so a theme
        //    change mid-recording blends smoothly):
        //      - Saturation Dark/Light, HueShift, Exposure Dark/Light,
        //        BlendSeconds.
        //      - Defaults mirror Transcribing so the out-of-box greyscale
        //        stays theme-consistent; tune independently if Recording
        //        needs a distinct palette.
        //
        //    No RecordingOpacity — UpdateLevel owns that channel from
        //    the mic RMS stream. ApplyVariant(Recording) deliberately
        //    skips the Opacity animation to avoid fighting UpdateLevel.
        public float  RecordingConicSpanTurns      { get; init; } = 0.5f;
        public float  RecordingConicLeadFadeTurns  { get; init; } = 1f;
        public float  RecordingConicTailFadeTurns  { get; init; } = 1f;
        public float  RecordingConicFadeCurve      { get; init; } = 2f;
        public bool   RecordingArcMirror           { get; init; } = true;
        public float  RecordingArcPhaseTurns       { get; init; } = 0f;
        public float  RecordingSaturationDark      { get; init; } = 0f;
        public float  RecordingSaturationLight     { get; init; } = 0f;
        public float  RecordingHueShiftTurns       { get; init; } = 0f;
        public float  RecordingExposureDark        { get; init; } = 0.7f;
        public float  RecordingExposureLight       { get; init; } = -1.2f;
        public double RecordingBlendSeconds        { get; init; } = 2;

        // Recording hue rotation — independent from arc rotation (which is
        // always frozen in Recording). 0 = hue frozen on HuePhaseTurns
        // (uniform grey at RecordingSaturation = 0, static tint at > 0).
        // > 0 = slow hue drift across the silhouette; pair with
        // RecordingSaturation Dark/Light > 0 for the chromatic effect to be
        // visible — at Saturation = 0 strict, RGB = (V, V, V) irrespective
        // of hue, so the hue rotates mathematically but reads identical.
        // Typical drift period for a calm "chatoiement" effect: 20–30 s.
        public double RecordingHuePeriodSeconds    { get; init; } = 0;
    }

}

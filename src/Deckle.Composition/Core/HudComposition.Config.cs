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
    // One stroke lives across Transcribing ↔ Rewriting: ApplyVariant blends
    // the Saturation, HueRotation and Exposure effects and the visual's
    // Opacity toward the variant's targets over BlendSeconds, with no surface
    // rebuild.
    //
    // ── Lexicon — vocabulary shared across the struct fields below ──────
<<<<<<< HEAD
    //   OklchLightness OKLab lightness, 0 = black, 1 = white.
    //   OklchChroma    OKLab chroma, 0 = grey. Values outside the sRGB gamut
    //                  are clamped per channel, which flattens those hues.
    //   HueStart       hue at 3 o'clock, in turns.
    //   HueRange       wheel slice. 1 = full rainbow, 0 = mono.
    //   WedgeCount     posterisation. 360 reads as smooth, 12 or 24 as steps.
    //   Span           arc length in turns. Mirror clamps it to 0.5.
    //   LeadFade/Tail  fade extents in turns. If Lead + Tail > Span both
    //                  scale down to meet at the arc middle (a bell).
    //   FadeCurve      pow(t, curve). 1 = linear, 3+ = crisp comet,
    //                  < 1 = near solid.
    //   Mirror         a second arc at +π for a symmetric double comet.
    //   PeriodSeconds  seconds per turn. Direction +1 CW, -1 CCW.
    //   PhaseTurns     start offset in turns.
    //   EaseP1/P2      cubic-bezier control points of one turn.
    //   MinSpeedFraction share of the mean angular velocity kept at every
    //                  instant. 0 = pure easing, 1 = constant rotation.
    //   Saturation     multiplier on the baked conic. 0 = greyscale.
    //   HueShiftTurns  runtime rotation of the colour wheel, in turns.
    //   Exposure       EV stops. ExposureEffect accepts [-2, +2].
    //   Opacity        SpriteVisual.Opacity, dims the whole stroke.
    //   BlendSeconds   duration of the blend into this variant.
=======
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
    //   OklchChroma    saturation ceiling in OKLab space. 0 = greyscale,
    //                  ~0.15 = vivid pastel. sRGB cannot show every hue
    //                  at one chroma: at L=0.75 yellows and blues run
    //                  out around 0.18, reds and greens further. The
    //                  painter lowers the chroma per hue until the
    //                  colour fits and keeps L (ColorSpace.
    //                  OklchToRgbInGamut), so a high ceiling means "as
    //                  vivid as each hue can be", never a clipped
    //                  channel — and the wheel desaturates to one grey.
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
>>>>>>> main

    // The explicit parameterless constructor is what makes the property
    // initialisers run on `new ConicArcStrokeConfig { ... }`; C# requires one
    // in a struct with initialisers.
    public readonly struct ConicArcStrokeConfig
    {
        public ConicArcStrokeConfig() {}

        // Shared across Recording, Transcribing and Rewriting.
        public float StrokeThickness { get; init; } = 4f;
        public float InsetDip { get; init; } = -2f;

        // ── Colour palette (paint-time, baked once) ──────────────────────
        // OKLCh, not HSV: its uniform luminance across hues keeps the
        // Saturation = 0 variants a flat grey instead of a top/bottom
        // grey gradient.
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
        // Out-in curve: both endpoint tangents have slope 3, so the Forever
        // loop is C¹ across the cycle seam and the rotation never pauses. An
        // in-out curve has zero slope at both ends and stalls at every seam.
        public float  HueEaseP1X         { get; init; } = 0.125f;
        public float  HueEaseP1Y         { get; init; } = 0.375f;
        public float  HueEaseP2X         { get; init; } = 0.875f;
        public float  HueEaseP2Y         { get; init; } = 0.625f;
        // 0 in shipping: the out-in curve never stalls. The Playground
        // exposes it for ease curves that do.
        public float  HueMinSpeedFraction { get; init; } = 0f;

        // ── Arc mask shape (white pie slice, alpha-ramped at both ends) ─
        public float  ConicSpanTurns     { get; init; } = 0.5f;
        public float  ConicLeadFadeTurns { get; init; } = 1f;
        public float  ConicTailFadeTurns { get; init; } = 1f;
        public float  ConicFadeCurve     { get; init; } = 4f;
        public bool   ArcMirror          { get; init; } = true;

        // ── Arc rotation — independent of the hue rotation; what the eye
        //    reads as the speed of the animation ──────────────────────────
        public double ArcPeriodSeconds   { get; init; } = 8.0;
        public float  ArcDirection       { get; init; } = 1f;
        public float  ArcPhaseTurns      { get; init; } = 0f;
        // Same out-in curve as HueEase, for the same seam continuity.
        public float  ArcEaseP1X         { get; init; } = 0.125f;
        public float  ArcEaseP1Y         { get; init; } = 0.375f;
        public float  ArcEaseP2X         { get; init; } = 0.875f;
        public float  ArcEaseP2Y         { get; init; } = 0.625f;
        public float  ArcMinSpeedFraction { get; init; } = 0f;

        // ── Clone-cone placement (the pinned digit reveal) ──────────────
        // Apex of the reveal cone as a fraction of host size, read by both
        // the live reveal and the ConicClone preview. (0.5, 0.5) reproduces
        // the contour's centred cone; (0, 0) radiates from the top-left
        // corner. The clone surface is sized so its inscribed circle reaches
        // every host corner from the apex; an angular gradient is unchanged
        // by that radial scaling.
        public float  CloneCentreXFraction { get; init; } = 196f / 272f; // 196 px on the 272-wide row
        public float  CloneCentreYFraction { get; init; } = 0f;          // apex at the row's top edge

        // ── Clone-cone palette — the reveal's own OKLCh lightness and
        //    chroma, so the swept digits can read brighter than the contour.
        //    The lift lives in the baked surface because ExposureEffect caps
        //    at +2 EV, too low for the grey-on-Tertiary sweep. ──────────────
        public float  CloneOklchLightness   { get; init; } = 0.9f;
        public float  CloneOklchChroma      { get; init; } = 0.3f;

        // ── Clone-cone rotation — only speeds and directions split from the
        //    contour; arc shape, ease and phase stay shared ────────────────
        public double CloneHuePeriodSeconds { get; init; } = 7.0;
        public float  CloneHueDirection     { get; init; } = -1f;
        public double CloneArcPeriodSeconds { get; init; } = 4.0;
        public float  CloneArcDirection     { get; init; } = -1f;

        // ── Rewriting variant — live effect targets ─────────────────────
        public float  RewritingSaturation       { get; init; } = 1f;
        public float  RewritingHueShiftTurns    { get; init; } = 0f;
        public float  RewritingExposure         { get; init; } = 0f;
        public float  RewritingOpacity          { get; init; } = 1f;
        public double RewritingBlendSeconds     { get; init; } = 2;

        // ── Transcribing variant — greyscale by default. Saturation and
        //    Exposure split Dark/Light: the grey must read against both
        //    substrates, so exposure lifts it on dark and lowers it on
        //    light ─────────────────────────────────────────────────────────
        public float  TranscribingSaturationDark  { get; init; } = 0f;
        public float  TranscribingSaturationLight { get; init; } = 0f;
        public float  TranscribingHueShiftTurns   { get; init; } = 0f;
        public float  TranscribingExposureDark    { get; init; } = 0.7f;
        public float  TranscribingExposureLight   { get; init; } = -1.2f;
        public float  TranscribingOpacity         { get; init; } = 1f;
        public double TranscribingBlendSeconds    { get; init; } = 2;

        // ── Recording variant — frozen arc, RMS-driven opacity ──────────
        //    The paint-time fields (ConicSpan, fades, FadeCurve, ArcMirror,
        //    ArcPhaseTurns) are fixed when CreateRecordingStroke runs; edit
        //    and rebuild to iterate. Phase 0 parks the lobes at 12 and 6
        //    o'clock (see CreateRecordingStroke). The runtime fields blend
        //    through ApplyVariant like the other variants.
        //    No RecordingOpacity: UpdateLevel owns opacity from the mic RMS,
        //    and ApplyVariant(Recording) skips Opacity so the two never fight.
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

        // 0 freezes the hue at HuePhaseTurns. A positive period drifts the
        // hue under the frozen lobes, visible only with RecordingSaturation
        // > 0: at saturation 0 every hue renders the same grey.
        public double RecordingHuePeriodSeconds    { get; init; } = 0;
    }

}

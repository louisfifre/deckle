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
    // Tweak the defaults to iterate on the visual.
    public static ProcessingStroke CreateProcessingStroke(
        Compositor compositor, Vector2 hostSize,
        ConicArcStrokeConfig? configOverride = null,
        bool animationsEnabled = true)
    {
        // Optional override used by HudChrono.RebuildStroke (HudPlayground
        // slider wiring). Null keeps the shipping defaults.
        return CreateConicArcStroke(
            compositor, hostSize,
            configOverride ?? new ConicArcStrokeConfig(),
            animationsEnabled: animationsEnabled);
    }

    // Recording stroke — the same double-comet pipeline as the processing
    // stroke, but with rotation frozen and the arc positioned at visual 12
    // and 6 o'clock. Opacity starts at 0 (invisible outline) and is driven
    // by ProcessingStroke.UpdateLevel from HudChrono's EMA-smoothed mic
    // RMS.
    //
    // All Recording-specific knobs live on the Recording* fields of
    // ConicArcStrokeConfig (see the struct section above). This factory
    // reads a default-constructed config and maps the Recording* paint-
    // time fields into the generic paint-time slots that
    // CreateConicArcStroke consumes — tweak the defaults on the struct
    // itself to iterate, just like the Transcribing*/Rewriting* knobs.
    //
    // ── Arc span + fades (from RecordingConic* defaults) ────────────────
    // Span 0.5 with Mirror = full 360° coverage split into two 180° arcs
    // that meet exactly at the sides (3 and 9 o'clock) with no overlap.
    // LeadFade/TailFade at 0.5 each get auto-scaled by the drawing code to
    // spanTurns/total = 0.25 each — pure bell, no solid core, peak opacity
    // at the lobe centre, smooth fade-out at both sides where the arcs
    // meet. Matches the design intent: the energy stays on the sides
    // and the strong fades take care of the centre.
    //
    // ── Arc phase math (from RecordingArcPhaseTurns default) ────────────
    // With Span 0.5, the source arc centre is at spanRadians/2 = 0.25·τ
    // (90° math). In Win2D's Y-down space that's (cos 90°, sin 90°)
    // = (0, 1) — straight down, 6 o'clock. Mirror at +π lands at 270° math
    // = (0, -1) = straight up, 12 o'clock.
    //
    // Target: lobes at visual 12 and 6 o'clock. Source already has a lobe
    // at 6 and mirror at 12, so we need a +0.5 turn rotation to realign
    // (the chirality flip: source 6 → visual 12, source 12 → visual 6).
    // RecordingArcPhaseTurns = 0.5 → 0.25 + 0.5 = 0.75·τ = 270° math =
    // 12 o'clock, mirror 0.75 + 0.5 = 1.25 ≡ 0.25·τ = 90° math =
    // 6 o'clock. ✓
    //
    // HuePhase doesn't matter at RecordingSaturation = 0 (uniform grey),
    // kept at 0. RecordingHuePeriodSeconds > 0 overrides the generic
    // HuePeriodSeconds to let the hue drift slowly under the frozen arc
    // lobes — requires RecordingSaturation > 0 to be visible.
    public static ProcessingStroke CreateRecordingStroke(
        Compositor compositor, Vector2 hostSize,
        ConicArcStrokeConfig? configOverride = null,
        bool animationsEnabled = true)
    {
        // `with` copies the caller's (default) config and overrides the
        // generic paint-time slots consumed by CreateConicArcStroke with
        // the Recording* paint-time values. All other fields inherit
        // their defaults — the Recording* runtime fields come through
        // unchanged and are read by ApplyVariant and by the initialVariant
        // seed path below.
        //
        // HudPlayground passes a pre-customized config via configOverride
        // so its Recording* sliders land on the live struct before the
        // `with` copy bakes them into the generic slots.
        var defaults = configOverride ?? new ConicArcStrokeConfig();
        bool hueRotates = defaults.RecordingHuePeriodSeconds > 0;
        var cfg = defaults with
        {
            ConicSpanTurns     = defaults.RecordingConicSpanTurns,
            ConicLeadFadeTurns = defaults.RecordingConicLeadFadeTurns,
            ConicTailFadeTurns = defaults.RecordingConicTailFadeTurns,
            ConicFadeCurve     = defaults.RecordingConicFadeCurve,
            ArcMirror          = defaults.RecordingArcMirror,
            ArcPhaseTurns      = defaults.RecordingArcPhaseTurns,
            // Only override the generic hue period when Recording wants
            // live hue motion. Otherwise the value is ignored anyway
            // (freezeHueRotation = true below).
            HuePeriodSeconds   = hueRotates
                ? defaults.RecordingHuePeriodSeconds
                : defaults.HuePeriodSeconds,
        };
        return CreateConicArcStroke(
            compositor, hostSize, cfg,
            freezeHueRotation: !hueRotates,
            freezeArcRotation: true,
            initialOpacity:    animationsEnabled ? 0f : 1f,
            initialVariant:    ProcessingVariant.Recording,
            animationsEnabled: animationsEnabled);
    }

    // Implementation of the double-comet pipeline driven by
    // CreateProcessingStroke. Composition has no conic-gradient brush, and
    // CompositionLinearGradientBrush paints in bounding-box coordinates
    // (colour varies along a fixed screen axis, not along the
    // rounded-rect perimeter), so a true rainbow that walks around the
    // stroke needs Win2D. SpriteShape.StrokeBrush also refuses any brush
    // other than Color / Linear / Radial gradient — no SurfaceBrush,
    // no MaskBrush, no EffectBrush. So we don't stroke a shape: we
    // paint surfaces off-screen and composite them on a plain SpriteVisual.
    //
    // Three off-screen surfaces:
    //   1. Conic surface   — full 360° colour ring, painted once as pie
    //                        wedges with HSV(hue, S, V), hue = angle / 2π.
    //                        At saturation = 0 it collapses to a uniform
    //                        greyscale surface (Transcribing).
    //   2. Arc mask        — white pie slice in [0, 2π·Span] with alpha
    //                        ramps at both ends (LeadFade, TailFade);
    //                        transparent outside. Optionally mirrored
    //                        at +π for a symmetric double-comet look
    //                        (ArcMirror).
    //   3. Stroke silhouette — rounded-rect stroke outline on a
    //                        transparent background, static.
    //
    // Each surface is sampled by its own CompositionSurfaceBrush. The
    // conic and arc brushes each drive their own TransformMatrix
    // ExpressionAnimation at independent rates (HuePeriodSeconds vs
    // ArcPeriodSeconds) — this is the whole point of the split: the arc
    // window sweeps around at one rate while the spectrum spins
    // underneath at another, so every hue eventually appears at the arc
    // head instead of being locked to a fixed source-angle. The stroke
    // silhouette does not rotate.
    //
    // Composition uses a Win2D AlphaMaskEffect graph in a
    // CompositionEffectBrush:
    //
    //   step1 = AlphaMask(Source = conic,   AlphaMask = arc)
    //   final = AlphaMask(Source = step1,   AlphaMask = strokeSilhouette)
    //
    // CompositionMaskBrush is not used because its Source property
    // forbids nesting another MaskBrush (Source must be
    // Color / Surface / NineGrid / Effect brush), and we need two layers
    // of alpha masking.
    //
    // No base stroke painted in this pipeline. The permanent HUD frame
    // is the DWM HWND border (DWMWA_BORDER_COLOR = DWMWA_COLOR_DEFAULT,
    // set in HudWindow.xaml.cs) — theme-aware, always on, visible
    // through the transparent regions between arcs. Painting a second
    // stroke here would occlude it with a non-theme-tracking colour.
    //
    // Surface sizing. The conic and arc surfaces are SQUARE, sized so
    // their inscribed circle contains the visual at every rotation:
    // pxSquare = ceil(√(pxW² + pxH²)) = visual diagonal. Smaller squares
    // leave visual corners outside the source at intermediate angles —
    // those pixels sample out of bounds and go transparent, producing
    // gaps that sweep with rotation. CompositionStretch.None centres the
    // source 1:1 on the visual (alignment ratios default to 0.5),
    // preserving the oversized brush-space footprint; any other stretch
    // mode rescales the source back down to the visual's extent and
    // defeats the coverage guarantee. The stroke silhouette surface is
    // pxW × pxH because it is not rotated, and its brush uses
    // CompositionStretch.Fill to map 1:1 onto the visual.
    //
    // Rotation via ExpressionAnimation because TransformMatrix is a
    // Matrix3x2 with no built-in KeyFrameAnimation type. A scalar Angle
    // on a CompositionPropertySet drives a 0 → 2π keyframe animation, and
    // the matrix is rebuilt every frame by an expression that rotates
    // around the VISUAL centre (innerSize / 2) — not the source centre.
    // CompositionSurfaceBrush.TransformMatrix is evaluated in SpriteVisual
    // space, AFTER Stretch/alignment have placed the source. Rotating
    // around the source centre instead would orbit the oversized square
    // around a point well outside the visual — the "half the stroke
    // missing at most phases" symptom we hit initially.
    // `freezeHueRotation` / `freezeArcRotation` pin the conic and arc
    // surfaces at their HuePhaseTurns / ArcPhaseTurns offsets via a static
    // TransformMatrix (no KeyFrameAnimation, no Composition-driven angular
    // motion). The two flags are independent so a variant can spin one
    // brush while freezing the other. Recording uses
    // freezeArcRotation=true (lobes parked at visual 12/6 o'clock) while
    // freezeHueRotation toggles on RecordingHuePeriodSeconds: 0 = frozen
    // grey, >0 = slow hue drift across the silhouette.
    //
    // `initialOpacity ≥ 0` overrides cfg.TranscribingOpacity for the
    // SpriteVisual's seed opacity. Sentinel value -1 falls back to the
    // Transcribing-baseline behaviour used by Processing strokes. Recording
    // passes 0 so the outline spawns invisible — UpdateLevel ramps it up
    // from there as mic RMS arrives.
    //
    // `initialVariant` picks which runtime-variant baseline seeds the
    // initial Saturation / Hue / Exposure values (and the effectProps
    // scalars that drive them). Transcribing is the default because a
    // processing stroke always enters the graph in that state; Recording
    // passes its own variant so cold-start paints with Recording*
    // values from frame 1, avoiding a Transcribing-to-Recording flash
    // before ApplyVariant runs. Rewriting is never seeded from cold —
    // it only ever follows a prior Transcribing via ApplyVariant blend.
    private static ProcessingStroke CreateConicArcStroke(
        Compositor compositor, Vector2 hostSize, ConicArcStrokeConfig cfg,
        bool              freezeHueRotation = false,
        bool              freezeArcRotation = false,
        float             initialOpacity = -1f,
        ProcessingVariant initialVariant = ProcessingVariant.Transcribing,
        bool              animationsEnabled = true)
    {
        var container = compositor.CreateContainerVisual();
        container.Size = hostSize;

        var innerSize = new Vector2(hostSize.X - 2f * InsetDip, hostSize.Y - 2f * InsetDip);
        // Math.Round (NOT Ceiling) — cf. the "Pixel-perfect sizing note" in
        // the file header. At fractional-DIP extents (e.g. hostSize.Y = 78.4
        // at 125 % DPI), Ceiling oversizes the silhouette surface by up to
        // 1 px, and Stretch.Fill then compresses it back into the visual —
        // scale < 1 — so the stroke's outer edge drawn at source y = pxH
        // lands inside the visual extent, producing a 1-dip gap on the
        // bottom/right (top/left stay flush because the origin pins y=0
        // and x=0 on both surface and visual).
        int pxW = Math.Max(1, (int)MathF.Round(innerSize.X));
        int pxH = Math.Max(1, (int)MathF.Round(innerSize.Y));
        // Rotating surfaces need side ≥ visual diagonal so the inscribed
        // circle of the source covers all four visual corners at every
        // rotation angle — cf. header comment. Compute from innerSize
        // directly (not from the rounded pxW/pxH) so a down-rounded pxW
        // or pxH at fractional DPI can't clip the diagonal coverage.
        int pxSquare = (int)Math.Ceiling(Math.Sqrt(
            (double)innerSize.X * innerSize.X +
            (double)innerSize.Y * innerSize.Y));

        var canvasDevice   = CanvasDevice.GetSharedDevice();
        // Wire the process-wide DeviceLost hook the first time we touch
        // the shared device. Idempotent — see EnsureDeviceLostHook header.
        EnsureDeviceLostHook(canvasDevice);
        var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, canvasDevice);

        // ── Surface 1: conic rainbow (full 360°, no arc carving here) ────
        // Surface painting extracted to PaintConicSurface so the naked
        // mask preview factory (CreateNakedMaskPreview, end of file) can
        // reuse the exact same logic without risking drift.
        var conicSurface = PaintConicSurface(canvasDevice, graphicsDevice, pxSquare, cfg);

        // ── Surface 2: arc mask (white pie slice, fade at both ends) ─────
        // Painted with straight alpha; Win2D premultiplies on write into a
        // Premultiplied surface. Colour channels are full white so the
        // mask's luminance does not tint the conic — only its alpha drives
        // the AlphaMaskEffect output. When cfg.ArcMirror is true, the
        // same arc is painted a second time at +π (180°) inside the same
        // surface, so both copies rotate together and stay in perfect
        // symmetry. Extracted to PaintArcMaskSurface for CreateNaked-
        // MaskPreview reuse.
        //
        // fillColor = Colors.White: downstream AlphaMaskEffect reads only
        // .A, so the mask's RGB is invisible to the shipping stroke. We
        // still write premultiplied-consistent bytes (white · α).
        var arcMaskSurface = PaintArcMaskSurface(
            canvasDevice, graphicsDevice, pxSquare, cfg, Colors.White);

        // ── Surface 3: stroke silhouette (static rounded-rect outline) ───
        // Inset by 0.5 dip so the 1-dip stroke is centred on the same path
        // the ShapeVisual would walk, preserving pixel-centre alignment
        // with the DWM frame.
        var strokeMaskSurface = graphicsDevice.CreateDrawingSurface(
            new Windows.Foundation.Size(pxW, pxH),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);

        using (var ds = CanvasComposition.CreateDrawingSession(strokeMaskSurface))
        {
            ds.Clear(Colors.Transparent);
            var rect = new Windows.Foundation.Rect(
                StrokeThickness / 2f,
                StrokeThickness / 2f,
                pxW - StrokeThickness,
                pxH - StrokeThickness);
            ds.DrawRoundedRectangle(rect, CornerRadiusDip, CornerRadiusDip, Colors.White, StrokeThickness);
        }

        // ── Surface brushes ──────────────────────────────────────────────
        // Stretch.None on the two rotating brushes preserves the oversized
        // pxSquare footprint in brush space. Stretch.Fill on the static
        // stroke brush maps 1:1 onto the visual (it is already pxW × pxH).
        var conicBrush = compositor.CreateSurfaceBrush(conicSurface);
        conicBrush.Stretch = CompositionStretch.None;

        var arcMaskBrush = compositor.CreateSurfaceBrush(arcMaskSurface);
        arcMaskBrush.Stretch = CompositionStretch.None;

        var strokeMaskBrush = compositor.CreateSurfaceBrush(strokeMaskSurface);
        strokeMaskBrush.Stretch = CompositionStretch.Fill;

        // ── Independent rotations for conic and arc ──────────────────────
        // Rotation around the VISUAL centre via T(-c) · R(θ) · T(+c)
        // composite, expressed in Composition's Matrix3x2 helpers.
        // CreateRotation takes radians; row-vector convention means
        // translations flank the rotation symmetrically. c = innerSize/2
        // because TransformMatrix is in SpriteVisual space, not source
        // pixel space.
        var visualCentre = new Vector2(innerSize.X / 2f, innerSize.Y / 2f);

        // Hue rotation — spin or freeze independently of the arc rotation.
        // Static TransformMatrix pins the brush at its HuePhaseTurns offset
        // with NO KeyFrameAnimation: same T(-c) · R(θ) · T(+c) composite
        // that StartRotation builds at t=0, baked into a one-shot matrix.
        // System.Numerics.Matrix3x2 uses row-vector convention, matching
        // Composition's expression-side maths.
        CompositionPropertySet? hueRotationProps = null;
        if (freezeHueRotation)
        {
            conicBrush.TransformMatrix =
                Matrix3x2.CreateTranslation(-visualCentre) *
                Matrix3x2.CreateRotation(MathF.Tau * cfg.HuePhaseTurns) *
                Matrix3x2.CreateTranslation( visualCentre);
        }
        else
        {
            hueRotationProps = StartRotation(
                compositor, conicBrush, visualCentre,
                cfg.HuePeriodSeconds,
                cfg.HueDirection,
                cfg.HuePhaseTurns,
                cfg.HueEaseP1X, cfg.HueEaseP1Y,
                cfg.HueEaseP2X, cfg.HueEaseP2Y,
                cfg.HueMinSpeedFraction,
                animationsEnabled: animationsEnabled);
        }

        // Arc rotation — spin or freeze independently of the hue rotation.
        // Recording always freezes the arc (lobes parked at visual 12/6
        // o'clock via RecordingArcPhaseTurns); Transcribing / Rewriting
        // always spin.
        CompositionPropertySet? arcRotationProps = null;
        if (freezeArcRotation)
        {
            arcMaskBrush.TransformMatrix =
                Matrix3x2.CreateTranslation(-visualCentre) *
                Matrix3x2.CreateRotation(MathF.Tau * cfg.ArcPhaseTurns) *
                Matrix3x2.CreateTranslation( visualCentre);
        }
        else
        {
            arcRotationProps = StartRotation(
                compositor, arcMaskBrush, visualCentre,
                cfg.ArcPeriodSeconds,
                cfg.ArcDirection,
                cfg.ArcPhaseTurns,
                cfg.ArcEaseP1X, cfg.ArcEaseP1Y,
                cfg.ArcEaseP2X, cfg.ArcEaseP2Y,
                cfg.ArcMinSpeedFraction,
                animationsEnabled: animationsEnabled);
        }

        // ── Effect graph ─────────────────────────────────────────────────
        //   Conic ──► Sat ──► Hue ──► Exp ──► AlphaMask(Arc) ──► AlphaMask(Stroke)
        //
        // Three live colour knobs (SaturationEffect, HueRotationEffect,
        // ExposureEffect) sit between the conic palette and the masking
        // stage. Each has its animable property exposed on the brush under
        // the name "<EffectName>.<PropertyName>", and bound (via a single
        // ExpressionAnimation per property) to a scalar on `effectProps`.
        // ProcessingStroke.ApplyVariant animates those scalars — never
        // the effect properties directly — so a "start from current value"
        // expression keyframe reads the PropertySet scalar, which always
        // holds the last animated value (the effect-brush property is a
        // derived mirror and `this.CurrentValue` on it is not reliable
        // mid-binding).
        //
        // Order rationale:
        //   Saturation BEFORE HueRotation so a greyscale target (Sat=0)
        //     already kills the palette — a HueRotation on grey is a no-op
        //     and doesn't waste GPU. If HueRotation were first, going grey
        //     would still shift hues on the way down.
        //   Exposure LAST so brightness shifts apply to the already-tinted
        //     colour. Putting it first would change the "source" brightness
        //     before Saturation read it, shifting the desaturated grey
        //     level away from the expected neutral.
        //
        // AlphaMaskEffect semantics: output = (Source.RGB, Source.A * Mask.A).
        // Both masks compound; the final pixel RGB is the (effect-modified)
        // conic colour, alpha = conic.A · arc.A · stroke.A.
        //
        // Initial effect values follow initialVariant's (Dark) baseline.
        // Processing strokes pass Transcribing (first processing state;
        // Rewriting can only follow a prior Transcribing via ApplyVariant
        // blend — seeding Rewriting values cold caused a visible rainbow
        // flash we fixed by defaulting to Transcribing). Recording strokes
        // pass their own variant so cold-start paints with Recording*
        // values from frame 1, skipping any Transcribing-to-Recording
        // flash before ApplyVariant runs.
        float seedSaturation = initialVariant switch
        {
            ProcessingVariant.Recording    => cfg.RecordingSaturationDark,
            ProcessingVariant.Rewriting    => cfg.RewritingSaturation,
            _                              => cfg.TranscribingSaturationDark,
        };
        float seedHueShiftTurns = initialVariant switch
        {
            ProcessingVariant.Recording    => cfg.RecordingHueShiftTurns,
            ProcessingVariant.Rewriting    => cfg.RewritingHueShiftTurns,
            _                              => cfg.TranscribingHueShiftTurns,
        };
        float seedExposure = initialVariant switch
        {
            ProcessingVariant.Recording    => cfg.RecordingExposureDark,
            ProcessingVariant.Rewriting    => cfg.RewritingExposure,
            _                              => cfg.TranscribingExposureDark,
        };

        var saturationEffect = new SaturationEffect
        {
            Name       = "Sat",
            Saturation = seedSaturation,
            Source     = new CompositionEffectSourceParameter("Conic"),
        };
        var hueEffect = new HueRotationEffect
        {
            Name   = "Hue",
            Angle  = seedHueShiftTurns * MathF.Tau,
            Source = saturationEffect,
        };
        var exposureEffect = new ExposureEffect
        {
            Name     = "Exp",
            Exposure = seedExposure,
            Source   = hueEffect,
        };
        var effectGraph = new AlphaMaskEffect
        {
            Source = new AlphaMaskEffect
            {
                Source    = exposureEffect,
                AlphaMask = new CompositionEffectSourceParameter("Arc"),
            },
            AlphaMask = new CompositionEffectSourceParameter("Stroke"),
        };

        // Declaring animable properties on the factory is what makes
        // effectBrush.StartAnimation("Sat.Saturation", …) legal — without
        // this list Composition rejects the property name.
        var effectFactory = compositor.CreateEffectFactory(
            effectGraph,
            new[] { "Sat.Saturation", "Hue.Angle", "Exp.Exposure" });
        var effectBrush = effectFactory.CreateBrush();
        effectBrush.SetSourceParameter("Conic",  conicBrush);
        effectBrush.SetSourceParameter("Arc",    arcMaskBrush);
        effectBrush.SetSourceParameter("Stroke", strokeMaskBrush);

        // PropertySet holds the live-animable scalars. ApplyVariant
        // animates these; the ExpressionAnimations below propagate their
        // values to the effect-brush properties every frame.
        var effectProps = compositor.CreatePropertySet();
        effectProps.InsertScalar("Saturation", seedSaturation);
        effectProps.InsertScalar("HueAngle",   seedHueShiftTurns * MathF.Tau);
        effectProps.InsertScalar("Exposure",   seedExposure);

        BindEffectProperty(compositor, effectBrush, "Sat.Saturation", effectProps, "Saturation");
        BindEffectProperty(compositor, effectBrush, "Hue.Angle",      effectProps, "HueAngle");
        BindEffectProperty(compositor, effectBrush, "Exp.Exposure",   effectProps, "Exposure");

        var strokeVisual = compositor.CreateSpriteVisual();
        strokeVisual.Size    = innerSize;
        strokeVisual.Offset  = new Vector3(InsetDip, InsetDip, 0f);
        strokeVisual.Brush   = effectBrush;
        // initialOpacity sentinel (-1) falls back to the Transcribing
        // baseline used by Processing strokes. Recording passes 0 so the
        // outline spawns invisible — UpdateLevel ramps it with mic RMS.
        strokeVisual.Opacity = initialOpacity >= 0f ? initialOpacity : cfg.TranscribingOpacity;

        container.Children.InsertAtTop(strokeVisual);
        return new ProcessingStroke(
            container, compositor, effectProps, strokeVisual, cfg,
            conicBrush, arcMaskBrush, strokeMaskBrush, effectBrush,
            conicSurface, arcMaskSurface, strokeMaskSurface,
            hueRotationProps, arcRotationProps,
            animationsEnabled);
    }
}

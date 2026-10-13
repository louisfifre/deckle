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
    public static ProcessingStroke CreateProcessingStroke(
        Compositor compositor, Vector2 hostSize,
        ConicArcStrokeConfig? configOverride = null,
        bool animationsEnabled = true)
    {
        return CreateConicArcStroke(
            compositor, hostSize,
            configOverride ?? new ConicArcStrokeConfig(),
            animationsEnabled: animationsEnabled);
    }

    // Recording stroke: the double-comet pipeline with the arc frozen and its
    // lobes on 12 and 6 o'clock. With animations on, opacity starts at 0 and
    // ProcessingStroke.UpdateLevel drives it from the mic RMS; with them off
    // the outline stays at 1.
    //
    // The Recording* paint-time fields replace the generic slots that
    // CreateConicArcStroke reads. With Span 0.5 and Mirror, the two 180°
    // lobes meet at 3 and 9 o'clock, and fades longer than the span scale
    // into a bell peaking at the lobe centre. At phase 0 the source lobe
    // centre sits at 90° in Win2D's Y-down space (6 o'clock) and its mirror
    // at 270° (12 o'clock).
    public static ProcessingStroke CreateRecordingStroke(
        Compositor compositor, Vector2 hostSize,
        ConicArcStrokeConfig? configOverride = null,
        bool animationsEnabled = true)
    {
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

    // The double-comet pipeline. Composition has no conic-gradient brush,
    // CompositionLinearGradientBrush varies along a fixed screen axis rather
    // than the perimeter, and SpriteShape.StrokeBrush accepts only colour,
    // linear and radial gradient brushes. So the stroke is painted into Win2D
    // surfaces and composited on a plain SpriteVisual:
    //   1. Conic       — the OKLCh colour ring, painted once.
    //   2. Arc mask    — alpha-ramped pie slice, optionally mirrored at +π.
    //   3. Silhouette  — the static rounded-rect outline.
    // The conic and the arc mask rotate at independent rates so every hue
    // eventually reaches the arc head.
    //
    // final = AlphaMask(AlphaMask(conic, arc), silhouette) in one
    // CompositionEffectBrush. CompositionMaskBrush cannot do it: its Source
    // refuses another MaskBrush, and two mask layers are needed.
    //
    // No base stroke is painted: the permanent HUD outline is the DWM border
    // (DWMWA_BORDER_COLOR = DWMWA_COLOR_DEFAULT in HudWindow), theme-aware and
    // visible between the arcs. A painted base stroke would hide it under a
    // colour that does not follow the theme.
    //
    // The rotating surfaces are squares as wide as the visual diagonal, so
    // their inscribed circle covers the visual at every angle; a smaller
    // square leaves transparent gaps sweeping with the rotation. Stretch.None
    // keeps that oversized footprint centred 1:1; any other stretch shrinks it
    // to the visual and loses the coverage. The silhouette surface is the
    // visual's size, with Stretch.Fill.
    //
    // TransformMatrix is evaluated in SpriteVisual space after Stretch and
    // alignment, so the rotation pivots on the visual centre (innerSize / 2).
    // Pivoting on the source centre orbits the square around a point outside
    // the visual.
    //
    // `freezeHueRotation` / `freezeArcRotation` pin a surface at its phase
    // with a static matrix. `initialOpacity` ≥ 0 replaces
    // cfg.TranscribingOpacity as the seed opacity. `initialVariant` seeds
    // Saturation / Hue / Exposure so the first frame already shows that
    // variant. Processing strokes seed Transcribing and reach Rewriting
    // through ApplyVariant's blend: a cold Rewriting seed flashes the rainbow.
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

        var innerSize = new Vector2(hostSize.X - 2f * cfg.InsetDip, hostSize.Y - 2f * cfg.InsetDip);
        // Round, not Ceiling: at a fractional DIP extent (78.4 at 125 % DPI)
        // Ceiling oversizes the silhouette surface by up to 1 px, Stretch.Fill
        // compresses it back, and the outer edge lands inside the visual as a
        // 1-dip gap on the bottom and right.
        int pxW = Math.Max(1, (int)MathF.Round(innerSize.X));
        int pxH = Math.Max(1, (int)MathF.Round(innerSize.Y));
        // From innerSize, not the rounded pxW/pxH, so a side rounded down
        // cannot clip the diagonal coverage.
        int pxSquare = (int)Math.Ceiling(Math.Sqrt(
            (double)innerSize.X * innerSize.X +
            (double)innerSize.Y * innerSize.Y));

        var canvasDevice   = CanvasDevice.GetSharedDevice();
        EnsureDeviceLostHook(canvasDevice);
        var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, canvasDevice);

        // ── Surface 1: conic rainbow (full 360°, no arc carving here) ────
        var conicSurface = PaintConicSurface(canvasDevice, graphicsDevice, pxSquare, cfg);

        // ── Surface 2: arc mask (white pie slice, fade at both ends) ─────
        // Both mirrored arcs share one surface, so they rotate together.
        // White fill: AlphaMaskEffect reads only the mask's alpha.
        var arcMaskSurface = PaintArcMaskSurface(
            canvasDevice, graphicsDevice, pxSquare, cfg, Colors.White);

        // ── Surface 3: stroke silhouette (static rounded-rect outline) ───
        var strokeMaskSurface = graphicsDevice.CreateDrawingSurface(
            new Windows.Foundation.Size(pxW, pxH),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);

        float strokeThickness = Math.Clamp(cfg.StrokeThickness, 0.5f, Math.Max(0.5f, Math.Min(pxW, pxH)));
        using (var ds = CanvasComposition.CreateDrawingSession(strokeMaskSurface))
        {
            ds.Clear(Colors.Transparent);
            var rect = new Windows.Foundation.Rect(
                strokeThickness / 2f,
                strokeThickness / 2f,
                pxW - strokeThickness,
                pxH - strokeThickness);
            ds.DrawRoundedRectangle(rect, CornerRadiusDip, CornerRadiusDip, Colors.White, strokeThickness);
        }

        // ── Surface brushes ──────────────────────────────────────────────
        var conicBrush = compositor.CreateSurfaceBrush(conicSurface);
        conicBrush.Stretch = CompositionStretch.None;

        var arcMaskBrush = compositor.CreateSurfaceBrush(arcMaskSurface);
        arcMaskBrush.Stretch = CompositionStretch.None;

        var strokeMaskBrush = compositor.CreateSurfaceBrush(strokeMaskSurface);
        strokeMaskBrush.Stretch = CompositionStretch.Fill;

        // ── Independent rotations for conic and arc ──────────────────────
        var visualCentre = new Vector2(innerSize.X / 2f, innerSize.Y / 2f);

        // Frozen: the composite StartRotation builds at t = 0, as a static
        // matrix. System.Numerics.Matrix3x2 uses the same row-vector
        // convention as Composition's expressions.
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
        // Saturation before HueRotation, so a greyscale target kills the
        // palette before any hue shift. Exposure last, so brightness applies
        // to the tinted colour and does not move the desaturated grey off
        // neutral. AlphaMaskEffect outputs (Source.RGB, Source.A · Mask.A).
        // Seeds use the Dark values; ApplyVariant corrects for the theme.
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

        var effectProps = compositor.CreatePropertySet();
        effectProps.InsertScalar("Saturation", seedSaturation);
        effectProps.InsertScalar("HueAngle",   seedHueShiftTurns * MathF.Tau);
        effectProps.InsertScalar("Exposure",   seedExposure);

        BindEffectProperty(compositor, effectBrush, "Sat.Saturation", effectProps, "Saturation");
        BindEffectProperty(compositor, effectBrush, "Hue.Angle",      effectProps, "HueAngle");
        BindEffectProperty(compositor, effectBrush, "Exp.Exposure",   effectProps, "Exposure");

        var strokeVisual = compositor.CreateSpriteVisual();
        strokeVisual.Size    = innerSize;
        strokeVisual.Offset  = new Vector3(cfg.InsetDip, cfg.InsetDip, 0f);
        strokeVisual.Brush   = effectBrush;
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

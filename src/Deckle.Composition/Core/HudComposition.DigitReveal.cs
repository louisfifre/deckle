using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;

namespace Deckle.Composition;

public static partial class HudComposition
{
    // ── Digit reveal — a chrono digit as glass onto a clone of the contour ───
    //
    // Grey ⊕ (Conic → Hue) → AlphaMask(Arc) → AlphaMask(Glyph): the
    // contour's pipeline (Factories.cs) with the digit glyph in place of the
    // rounded-rect silhouette.
    //
    // The material is shared: the mix, hue and grey slots bind to the
    // stroke's EffectProps, so the digits grey and colour in lock-step with
    // the contour. Surfaces and rotations are cloned (RevealConeMaterial),
    // placed at the config apex, sized to cover every host corner from it,
    // and shared across the six digits so they read one cone in phase.
    //
    // The arc mask is what makes the reveal visible in Transcribing: the
    // grey material is a flat field whose rotation does not show.
    //
    // The glyph mask is the readout TextBlock's own alpha
    // (TextBlock.GetAlphaMask), fed to the graph as its last AlphaMask source,
    // so the digit is pixel-identical to the readout. A Win2D-painted glyph
    // comes out slanted: DirectWrite's file loader resolves the variable
    // font's typographic family to its default, oblique, instance, while XAML
    // reaches the upright face through the WWS family name.
    //
    // The glyph stays inside the effect graph: a built effect brush handed to
    // a CompositionMaskBrush as Source renders transparent.
    public sealed class DigitRevealVisual : IDisposable
    {
        public SpriteVisual Visual { get; }

        private readonly CompositionSurfaceBrush _conicBrush;
        private readonly CompositionSurfaceBrush _arcBrush;
        private readonly CompositionEffectBrush  _effectBrush;
        private bool _disposed;

        internal DigitRevealVisual(
            SpriteVisual visual,
            CompositionSurfaceBrush conicBrush,
            CompositionSurfaceBrush arcBrush,
            CompositionEffectBrush  effectBrush)
        {
            Visual       = visual;
            _conicBrush  = conicBrush;
            _arcBrush    = arcBrush;
            _effectBrush = effectBrush;
        }

        // Rounded to 3 decimals so float noise (0.9999997) does not re-set
        // Opacity, and re-invalidate the render pass, every vsync.
        public void SetHeat(float heat)
        {
            double rounded = Math.Round(Math.Clamp(heat, 0f, 1f), 3);
            if (Visual.Opacity != rounded)
                Visual.Opacity = (float)rounded;
        }

        // Disposes only this reveal's brushes and sprite. The clone surfaces and
        // rotations belong to the RevealConeMaterial, the EffectProps to the
        // ProcessingStroke, the glyph mask to the TextBlock. The caller disposes
        // every reveal before the material and the stroke, or a live brush
        // would sample freed memory.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            UnbindMaterialMix(_effectBrush);
            try { _conicBrush.StopAnimation("TransformMatrix"); } catch { }
            try { _arcBrush.StopAnimation("TransformMatrix");   } catch { }

            try { _effectBrush.Dispose(); } catch { }
            try { _conicBrush.Dispose();  } catch { }
            try { _arcBrush.Dispose();    } catch { }
            try { Visual.Dispose(); } catch { }
        }
    }

    // `material` is shared by the six digits and owned by the caller.
    // `conicCentreInSpriteSpace` = apex − cellOffset: both brushes spin around
    // it, so every digit samples its own slice of one cone and the comet stays
    // concentric with it.
    public static DigitRevealVisual CreateDigitReveal(
        Compositor compositor,
        ProcessingStroke stroke,
        RevealConeMaterial material,
        CompositionBrush glyphAlphaMask,
        Vector2 spriteSize,
        Vector2 conicCentreInSpriteSpace)
    {
        var cfg = stroke.Config;
        var negHalf = -spriteSize / 2f;

        var conicBrush = compositor.CreateSurfaceBrush(material.ConicSurface);
        conicBrush.Stretch = CompositionStretch.None;
        BindPlacedRotation(compositor, conicBrush, material.HueProps,
            negHalf, conicCentreInSpriteSpace, cfg.HueMinSpeedFraction);

        var arcBrush = compositor.CreateSurfaceBrush(material.ArcSurface);
        arcBrush.Stretch = CompositionStretch.None;
        BindPlacedRotation(compositor, arcBrush, material.ArcProps,
            negHalf, conicCentreInSpriteSpace, cfg.ArcMinSpeedFraction);

        // Seeded at the Transcribing resting values; the bindings below take
        // over from the first frame.
        var arcMaskedGraph = new AlphaMaskEffect
        {
            Source    = BuildMaterialMix(
                new CompositionEffectSourceParameter("Conic"),
                0f, 0f, GreyMaterialColor(cfg.GreyLightnessDark)),
            AlphaMask = new CompositionEffectSourceParameter("Arc"),
        };
        var glyphMaskedGraph = new AlphaMaskEffect
        {
            Source    = arcMaskedGraph,
            AlphaMask = new CompositionEffectSourceParameter("Glyph"),
        };

        var effectFactory = compositor.CreateEffectFactory(
            glyphMaskedGraph, MaterialMixAnimableProperties);
        var effectBrush = effectFactory.CreateBrush();
        effectBrush.SetSourceParameter("Conic", conicBrush);
        effectBrush.SetSourceParameter("Arc",   arcBrush);
        effectBrush.SetSourceParameter("Glyph", glyphAlphaMask);

        BindMaterialMix(compositor, effectBrush, stroke.EffectProps);

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size    = spriteSize;
        sprite.Brush   = effectBrush;
        sprite.Opacity = 0f;   // the owner pins this to 1 after attachment

        return new DigitRevealVisual(sprite, conicBrush, arcBrush, effectBrush);
    }

    // The clone cone the six digit reveals sample: conic and arc-mask surfaces
    // plus hue and arc rotations at the clone periods. The caller disposes it
    // after every reveal that samples it (TearDownReveals), or a render tick
    // samples freed memory.
    public sealed class RevealConeMaterial : IDisposable
    {
        public CompositionDrawingSurface ConicSurface { get; }
        public CompositionDrawingSurface ArcSurface   { get; }
        public CompositionPropertySet    HueProps     { get; }
        public CompositionPropertySet    ArcProps     { get; }

        private bool _disposed;

        internal RevealConeMaterial(
            CompositionDrawingSurface conicSurface,
            CompositionDrawingSurface arcSurface,
            CompositionPropertySet hueProps,
            CompositionPropertySet arcProps)
        {
            ConicSurface = conicSurface;
            ArcSurface   = arcSurface;
            HueProps     = hueProps;
            ArcProps     = arcProps;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { HueProps.StopAnimation("Linear"); } catch { }
            try { HueProps.StopAnimation("Eased");  } catch { }
            try { ArcProps.StopAnimation("Linear"); } catch { }
            try { ArcProps.StopAnimation("Eased");  } catch { }

            try { HueProps.Dispose();     } catch { }
            try { ArcProps.Dispose();     } catch { }
            try { ConicSurface.Dispose(); } catch { }
            try { ArcSurface.Dispose();   } catch { }
        }
    }

    // Both surfaces share one CoverageSquareSide square so the comet mask stays
    // concentric with the cone. The arc fill is white: the graph reads only its
    // alpha.
    public static RevealConeMaterial BuildRevealConeMaterial(
        Compositor compositor,
        Vector2 hostSize,
        Vector2 apex,
        ConicArcStrokeConfig cfg,
        bool animationsEnabled = true)
    {
        int pxSquare = CoverageSquareSide(hostSize, apex);

        var canvasDevice = CanvasDevice.GetSharedDevice();
        EnsureDeviceLostHook(canvasDevice);
        var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(
            compositor, canvasDevice);

        var conicSurface = PaintConicSurface  (canvasDevice, graphicsDevice, pxSquare,
                                               CloneSurfaceConfig(cfg));
        var arcSurface   = PaintArcMaskSurface(canvasDevice, graphicsDevice, pxSquare, cfg,
                                               Microsoft.UI.Colors.White);

        var hueProps = CreateRotationPropertySet(
            compositor, cfg.CloneHuePeriodSeconds, cfg.CloneHueDirection, cfg.HuePhaseTurns,
            cfg.HueEaseP1X, cfg.HueEaseP1Y, cfg.HueEaseP2X, cfg.HueEaseP2Y,
            animationsEnabled);
        var arcProps = CreateRotationPropertySet(
            compositor, cfg.CloneArcPeriodSeconds, cfg.CloneArcDirection, cfg.ArcPhaseTurns,
            cfg.ArcEaseP1X, cfg.ArcEaseP1Y, cfg.ArcEaseP2X, cfg.ArcEaseP2Y,
            animationsEnabled);

        return new RevealConeMaterial(conicSurface, arcSurface, hueProps, arcProps);
    }

    // Shared by the live reveal and the ConicClone preview so their surfaces
    // cannot drift.
    private static ConicArcStrokeConfig CloneSurfaceConfig(ConicArcStrokeConfig cfg)
        => cfg with { OklchLightness = cfg.CloneOklchLightness, OklchChroma = cfg.CloneOklchChroma };

    // Side of the square whose inscribed circle reaches every corner of `frame`
    // from `centre`, at any rotation angle: off the surface a brush samples
    // transparent. Shared by the reveal, the ConicClone preview and the
    // countdown so they cannot drift.
    private static int CoverageSquareSide(Vector2 frame, Vector2 centre)
    {
        var corners = new[]
        {
            new Vector2(0f, 0f),       new Vector2(frame.X, 0f),
            new Vector2(0f, frame.Y),  new Vector2(frame.X, frame.Y),
        };
        float maxCornerDist = 0f;
        foreach (var corner in corners)
            maxCornerDist = MathF.Max(maxCornerDist, Vector2.Distance(centre, corner));
        return Math.Max(1, (int)Math.Ceiling(2.0 * maxCornerDist));
    }
}

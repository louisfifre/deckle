using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Windows.Graphics.Effects;

namespace Deckle.Composition;

public static partial class HudComposition
{
    // ╔════════════════════════════════════════════════════════════════════╗
    // ║  Conic clone preview (Playground only)                             ║
    // ╚════════════════════════════════════════════════════════════════════╝
    // An independent double comet (own conic and arc surfaces, brushes and
    // rotations) built from the same config fields as the pinned digit reveal:
    // clone placement, clone periods, clone palette. The sprite clips to the
    // row frame, so it shows exactly the slice the digit row samples. The
    // grading is a static seed for the chosen variant; toggling the variant
    // rebuilds the preview.
    //
    // The caller holds the bundle and Disposes it before mounting a
    // replacement: it carries four Forever animations (see NakedPreview).
    public sealed class ConicClonePreview : IDisposable
    {
        public ContainerVisual Container { get; }

        private readonly SpriteVisual _sprite;
        private readonly CompositionSurfaceBrush _conicBrush;
        private readonly CompositionSurfaceBrush _arcBrush;
        private readonly CompositionEffectBrush _effectBrush;
        private readonly CompositionDrawingSurface _conicSurface;
        private readonly CompositionDrawingSurface _arcSurface;
        private readonly CompositionPropertySet _hueRotationProps;
        private readonly CompositionPropertySet _arcRotationProps;

        private bool _disposed;

        internal ConicClonePreview(
            ContainerVisual container,
            SpriteVisual sprite,
            CompositionSurfaceBrush conicBrush,
            CompositionSurfaceBrush arcBrush,
            CompositionEffectBrush effectBrush,
            CompositionDrawingSurface conicSurface,
            CompositionDrawingSurface arcSurface,
            CompositionPropertySet hueRotationProps,
            CompositionPropertySet arcRotationProps)
        {
            Container         = container;
            _sprite           = sprite;
            _conicBrush       = conicBrush;
            _arcBrush         = arcBrush;
            _effectBrush      = effectBrush;
            _conicSurface     = conicSurface;
            _arcSurface       = arcSurface;
            _hueRotationProps = hueRotationProps;
            _arcRotationProps = arcRotationProps;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop the TransformMatrix expressions first so stopping the
            // Linear / Eased scalars they read can't race a live evaluation.
            try { _conicBrush.StopAnimation("TransformMatrix"); } catch { }
            try { _arcBrush.StopAnimation("TransformMatrix");   } catch { }
            try { _hueRotationProps.StopAnimation("Linear"); } catch { }
            try { _hueRotationProps.StopAnimation("Eased");  } catch { }
            try { _arcRotationProps.StopAnimation("Linear"); } catch { }
            try { _arcRotationProps.StopAnimation("Eased");  } catch { }

            try { _effectBrush.Dispose();      } catch { }
            try { _conicBrush.Dispose();       } catch { }
            try { _arcBrush.Dispose();         } catch { }
            try { _conicSurface.Dispose();     } catch { }
            try { _arcSurface.Dispose();       } catch { }
            try { _hueRotationProps.Dispose(); } catch { }
            try { _arcRotationProps.Dispose(); } catch { }
            try { _sprite.Dispose();           } catch { }
            try { Container.Dispose();         } catch { }
        }
    }

    public static ConicClonePreview CreateConicClonePreview(
        Compositor compositor, Vector2 hudSize,
        ConicArcStrokeConfig cfg,
        ProcessingVariant gradeVariant, bool isDark,
        bool animationsEnabled = true)
    {
        var coneCentre = new Vector2(
            cfg.CloneCentreXFraction * hudSize.X,
            cfg.CloneCentreYFraction * hudSize.Y);

        int pxSquare = CoverageSquareSide(hudSize, coneCentre);

        var canvasDevice = CanvasDevice.GetSharedDevice();
        EnsureDeviceLostHook(canvasDevice);
        var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(
            compositor, canvasDevice);

        var conicSurface = PaintConicSurface(canvasDevice, graphicsDevice, pxSquare,
            CloneSurfaceConfig(cfg));
        var arcSurface   = PaintArcMaskSurface(
            canvasDevice, graphicsDevice, pxSquare, cfg, Microsoft.UI.Colors.White);

        // Stretch.None keeps each oversized surface centred on the sprite
        // (hudSize / 2); the placement transform then parks that centre at
        // coneCentre and spins around it.
        var conicBrush = compositor.CreateSurfaceBrush(conicSurface);
        conicBrush.Stretch = CompositionStretch.None;
        var hueRotationProps = StartRotation(
            compositor, conicBrush, hudSize / 2f,
            cfg.CloneHuePeriodSeconds,
            cfg.CloneHueDirection,
            cfg.HuePhaseTurns,
            cfg.HueEaseP1X, cfg.HueEaseP1Y,
            cfg.HueEaseP2X, cfg.HueEaseP2Y,
            cfg.HueMinSpeedFraction,
            placement: coneCentre,
            animationsEnabled: animationsEnabled);

        var arcBrush = compositor.CreateSurfaceBrush(arcSurface);
        arcBrush.Stretch = CompositionStretch.None;
        var arcRotationProps = StartRotation(
            compositor, arcBrush, hudSize / 2f,
            cfg.CloneArcPeriodSeconds,
            cfg.CloneArcDirection,
            cfg.ArcPhaseTurns,
            cfg.ArcEaseP1X, cfg.ArcEaseP1Y,
            cfg.ArcEaseP2X, cfg.ArcEaseP2Y,
            cfg.ArcMinSpeedFraction,
            placement: coneCentre,
            animationsEnabled: animationsEnabled);

        var gradedGraph = BuildVariantGrading(
            new CompositionEffectSourceParameter("Conic"), cfg, gradeVariant, isDark);
        var maskedGraph = new AlphaMaskEffect
        {
            Source    = gradedGraph,
            AlphaMask = new CompositionEffectSourceParameter("Arc"),
        };
        var effectFactory = compositor.CreateEffectFactory(maskedGraph);
        var effectBrush = effectFactory.CreateBrush();
        effectBrush.SetSourceParameter("Conic", conicBrush);
        effectBrush.SetSourceParameter("Arc",   arcBrush);

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size  = hudSize;          // clips the cone to the row frame
        sprite.Brush = effectBrush;

        var container = compositor.CreateContainerVisual();
        container.Size = hudSize;
        container.Children.InsertAtTop(sprite);

        return new ConicClonePreview(
            container, sprite, conicBrush, arcBrush, effectBrush,
            conicSurface, arcSurface, hueRotationProps, arcRotationProps);
    }

    // The contour's Saturation → Hue → Exposure chain, seeded at a variant's
    // resting values. Static: previews do not blend between states.
    private static IGraphicsEffect BuildVariantGrading(
        IGraphicsEffectSource conicSource,
        ConicArcStrokeConfig cfg, ProcessingVariant variant, bool isDark)
    {
        float sat, hueTurns, exposure;
        if (variant == ProcessingVariant.Rewriting)
        {
            sat      = cfg.RewritingSaturation;
            hueTurns = cfg.RewritingHueShiftTurns;
            exposure = cfg.RewritingExposure;
        }
        else // Transcribing (and Recording, unused here) — greyscale baseline
        {
            sat      = isDark ? cfg.TranscribingSaturationDark : cfg.TranscribingSaturationLight;
            hueTurns = cfg.TranscribingHueShiftTurns;
            exposure = isDark ? cfg.TranscribingExposureDark   : cfg.TranscribingExposureLight;
        }

        var saturationEffect = new SaturationEffect
        {
            Name       = "Sat",
            Saturation = sat,
            Source     = conicSource,
        };
        var hueEffect = new HueRotationEffect
        {
            Name   = "Hue",
            Angle  = hueTurns * MathF.Tau,
            Source = saturationEffect,
        };
        return new ExposureEffect
        {
            Name     = "Exp",
            Exposure = exposure,
            Source   = hueEffect,
        };
    }
}

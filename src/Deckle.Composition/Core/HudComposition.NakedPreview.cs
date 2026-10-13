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
    // ║  Naked mask diagnostic (Playground only)                           ║
    // ╚════════════════════════════════════════════════════════════════════╝
    // Shows the conic and arc-mask surfaces CreateConicArcStroke composites
    // behind the silhouette. The silhouette clips every sample and hides any
    // rotation wobble or off-centre footprint; here the whole pxSquare
    // footprint rotates in the open.
    public enum NakedMaskPart
    {
        Conic    = 1,   // the conic surface, graded for the chosen variant, full square
        ArcMask  = 2,   // alpha-ramped pie slice(s), monochrome
        Combined = 3,   // Conic ⊗ ArcMask, no stroke silhouette
    }

    // Each rotation PropertySet carries two Forever ScalarKeyFrameAnimations
    // that the compositor keeps until they are stopped. Orphaned on every
    // rebuild, they accumulate until the compositor saturates and every
    // Forever animation in the window freezes. The caller disposes the bundle
    // before mounting a replacement and before its window closes.
    public sealed class NakedPreview : IDisposable
    {
        public ContainerVisual Container { get; }

        private readonly SpriteVisual _sprite;
        private readonly CompositionSurfaceBrush _conicBrush;
        private readonly CompositionSurfaceBrush _arcMaskBrush;
        private readonly CompositionEffectBrush? _effectBrush;
        private readonly CompositionDrawingSurface _conicSurface;
        private readonly CompositionDrawingSurface _arcMaskSurface;
        // Null when the cone is frozen (static TransformMatrix, no animation).
        private readonly CompositionPropertySet? _conicRotationProps;
        private readonly CompositionPropertySet _arcRotationProps;

        private bool _disposed;

        internal NakedPreview(
            ContainerVisual container,
            SpriteVisual sprite,
            CompositionSurfaceBrush conicBrush,
            CompositionSurfaceBrush arcMaskBrush,
            CompositionEffectBrush? effectBrush,
            CompositionDrawingSurface conicSurface,
            CompositionDrawingSurface arcMaskSurface,
            CompositionPropertySet? conicRotationProps,
            CompositionPropertySet arcRotationProps)
        {
            Container            = container;
            _sprite              = sprite;
            _conicBrush          = conicBrush;
            _arcMaskBrush        = arcMaskBrush;
            _effectBrush         = effectBrush;
            _conicSurface        = conicSurface;
            _arcMaskSurface      = arcMaskSurface;
            _conicRotationProps  = conicRotationProps;
            _arcRotationProps    = arcRotationProps;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop the TransformMatrix expressions before the scalars they
            // read, so stopping the scalars cannot race a live evaluation.
            try { _conicBrush.StopAnimation("TransformMatrix");   } catch { }
            try { _arcMaskBrush.StopAnimation("TransformMatrix"); } catch { }

            try { _conicRotationProps?.StopAnimation("Linear"); } catch { }
            try { _conicRotationProps?.StopAnimation("Eased");  } catch { }
            try { _arcRotationProps.StopAnimation("Linear");    } catch { }
            try { _arcRotationProps.StopAnimation("Eased");     } catch { }

            // Effect brush first: it binds the source brushes.
            try { _effectBrush?.Dispose();   } catch { }
            try { _conicBrush.Dispose();     } catch { }
            try { _arcMaskBrush.Dispose();   } catch { }

            try { _conicSurface.Dispose();   } catch { }
            try { _arcMaskSurface.Dispose(); } catch { }

            try { _conicRotationProps?.Dispose(); } catch { }
            try { _arcRotationProps.Dispose();    } catch { }

            try { _sprite.Dispose();    } catch { }
            try { Container.Dispose();  } catch { }
        }
    }

    // A pxSquare container (the stroke's diagonal footprint) holding one
    // sprite whose brush `part` picks. Both brushes rotate around the sprite
    // centre, so a surface painted off that centre shows as a drifting lobe.
    //
    // `arcFillColor` is a theme-legible opaque colour for the ArcMask part,
    // which draws the mask directly; Combined reads only its alpha.
    // `freezeHueRotation` pins the cone at HuePhaseTurns, the pose
    // CreateRecordingStroke takes when RecordingHuePeriodSeconds is 0.
    public static NakedPreview CreateNakedMaskPreview(
        Compositor compositor, Vector2 hudSize,
        ConicArcStrokeConfig cfg, NakedMaskPart part,
        Color arcFillColor,
        ProcessingVariant gradeVariant, bool isDark,
        bool animationsEnabled = true,
        bool freezeHueRotation = false)
    {
        // Same pxSquare math as CreateConicArcStroke: a different footprint
        // would show a geometry the stroke never samples.
        var innerSize = new Vector2(hudSize.X - 2f * cfg.InsetDip, hudSize.Y - 2f * cfg.InsetDip);
        int pxSquare = (int)Math.Ceiling(Math.Sqrt(
            (double)innerSize.X * innerSize.X +
            (double)innerSize.Y * innerSize.Y));

        var canvasDevice   = CanvasDevice.GetSharedDevice();
        EnsureDeviceLostHook(canvasDevice);
        var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(
            compositor, canvasDevice);

        var container = compositor.CreateContainerVisual();
        container.Size = new Vector2(pxSquare, pxSquare);

        var conicSurface   = PaintConicSurface  (canvasDevice, graphicsDevice, pxSquare, cfg);
        var arcMaskSurface = PaintArcMaskSurface(canvasDevice, graphicsDevice, pxSquare, cfg, arcFillColor);

        // Fill, unlike the stroke's Stretch.None: here the sprite is pxSquare,
        // the same size as the surface.
        var conicBrush = compositor.CreateSurfaceBrush(conicSurface);
        conicBrush.Stretch = CompositionStretch.Fill;

        var arcMaskBrush = compositor.CreateSurfaceBrush(arcMaskSurface);
        arcMaskBrush.Stretch = CompositionStretch.Fill;

        var centre = new Vector2(pxSquare / 2f, pxSquare / 2f);

        CompositionPropertySet? conicRotationProps = null;
        if (freezeHueRotation)
        {
            // Same one-shot pose as CreateConicArcStroke's freezeHueRotation.
            conicBrush.TransformMatrix =
                Matrix3x2.CreateTranslation(-centre) *
                Matrix3x2.CreateRotation(MathF.Tau * cfg.HuePhaseTurns) *
                Matrix3x2.CreateTranslation(centre);
        }
        else
        {
            conicRotationProps = StartRotation(
                compositor, conicBrush, centre,
                cfg.HuePeriodSeconds,
                cfg.HueDirection,
                cfg.HuePhaseTurns,
                cfg.HueEaseP1X, cfg.HueEaseP1Y,
                cfg.HueEaseP2X, cfg.HueEaseP2Y,
                cfg.HueMinSpeedFraction,
                animationsEnabled: animationsEnabled);
        }
        var arcRotationProps = StartRotation(
            compositor, arcMaskBrush, centre,
            cfg.ArcPeriodSeconds,
            cfg.ArcDirection,
            cfg.ArcPhaseTurns,
            cfg.ArcEaseP1X, cfg.ArcEaseP1Y,
            cfg.ArcEaseP2X, cfg.ArcEaseP2Y,
            cfg.ArcMinSpeedFraction,
            animationsEnabled: animationsEnabled);

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size = new Vector2(pxSquare, pxSquare);

        CompositionEffectBrush? effectBrush = null;
        switch (part)
        {
            case NakedMaskPart.Conic:
            {
                var graded = BuildVariantGrading(
                    new CompositionEffectSourceParameter("Conic"), cfg, gradeVariant, isDark);
                var ef = compositor.CreateEffectFactory(graded);
                effectBrush = ef.CreateBrush();
                effectBrush.SetSourceParameter("Conic", conicBrush);
                sprite.Brush = effectBrush;
                break;
            }
            case NakedMaskPart.ArcMask:
                // Ungraded: the variant toggle leaves the monochrome mask
                // untouched.
                sprite.Brush = arcMaskBrush;
                break;
            case NakedMaskPart.Combined:
            {
                var graded = BuildVariantGrading(
                    new CompositionEffectSourceParameter("Conic"), cfg, gradeVariant, isDark);
                var effectGraph = new AlphaMaskEffect
                {
                    Source    = graded,
                    AlphaMask = new CompositionEffectSourceParameter("Arc"),
                };
                var effectFactory = compositor.CreateEffectFactory(effectGraph);
                effectBrush = effectFactory.CreateBrush();
                effectBrush.SetSourceParameter("Conic", conicBrush);
                effectBrush.SetSourceParameter("Arc",   arcMaskBrush);
                sprite.Brush = effectBrush;
                break;
            }
        }

        container.Children.InsertAtTop(sprite);

        return new NakedPreview(
            container, sprite,
            conicBrush, arcMaskBrush, effectBrush,
            conicSurface, arcMaskSurface,
            conicRotationProps, arcRotationProps);
    }
}

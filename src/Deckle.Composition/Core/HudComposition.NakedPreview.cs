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
    // ║  Naked mask diagnostic (HudPlayground only)                        ║
    // ╚════════════════════════════════════════════════════════════════════╝
    // Exposes the raw conic and arc-mask surfaces — the same ones
    // CreateConicArcStroke composites behind the stroke silhouette — so
    // the developer can verify in the playground whether the brush
    // geometry is centred on its own rotation axis. The shipping stroke clips every
    // sample to the rounded-rect silhouette, which hides any rotation
    // wobble or off-centre brush footprint; the naked preview removes that
    // silhouette and lets the full pxSquare × pxSquare footprint rotate
    // openly. Not referenced by shipping code — only the playground's
    // Naked rail wires it up.
    public enum NakedMaskPart
    {
        Conic    = 1,   // raw 360° HSV rainbow ring, full square
        ArcMask  = 2,   // alpha-ramped pie slice(s), monochrome
        Combined = 3,   // Conic ⊗ ArcMask, no stroke silhouette
    }

    // Disposable bundle returned by CreateNakedMaskPreview. Mirrors the
    // ProcessingStroke pattern on purpose: the rotation PropertySets
    // returned by StartRotation drive two Forever ScalarKeyFrameAnimations
    // that the compositor keeps live until they are explicitly stopped.
    // If the caller (PlaygroundWindow) lets the PropertySets fall out of
    // scope on every rebuild, the compositor accumulates orphan
    // animations; after enough slider moves it saturates and Forever
    // animations across the whole window silently freeze — which is the
    // "Conic preview frozen mid-animation" regression that was reported.
    //
    // Ownership convention: the caller holds the NakedPreview while the
    // bundle is mounted on the visual tree, disposes it BEFORE replacing
    // it with a fresh one (and before the host Window closes). Dispose
    // stops both rotations' Linear + Eased animations and releases every
    // Composition object the bundle allocated.
    public sealed class NakedPreview : IDisposable
    {
        public ContainerVisual Container { get; }

        private readonly SpriteVisual _sprite;
        private readonly CompositionSurfaceBrush _conicBrush;
        private readonly CompositionSurfaceBrush _arcMaskBrush;
        private readonly CompositionEffectBrush? _effectBrush;
        private readonly CompositionDrawingSurface _conicSurface;
        private readonly CompositionDrawingSurface _arcMaskSurface;
        private readonly CompositionPropertySet _conicRotationProps;
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
            CompositionPropertySet conicRotationProps,
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

            // 1. Stop every Forever animation so the native scheduler drops
            //    its refs. ExpressionAnimation on the brush's TransformMatrix
            //    is what binds the PropertySet scalars; stop it first so
            //    stopping the scalars doesn't race against a live read.
            try { _conicBrush.StopAnimation("TransformMatrix");   } catch { }
            try { _arcMaskBrush.StopAnimation("TransformMatrix"); } catch { }

            try { _conicRotationProps.StopAnimation("Linear"); } catch { }
            try { _conicRotationProps.StopAnimation("Eased");  } catch { }
            try { _arcRotationProps.StopAnimation("Linear");   } catch { }
            try { _arcRotationProps.StopAnimation("Eased");    } catch { }

            // 2. Dispose in the same order the shipping stroke uses:
            //    effect brush first (it binds the source brushes), then
            //    surface brushes, then surfaces, then property sets, then
            //    the sprite + container last.
            try { _effectBrush?.Dispose();   } catch { }
            try { _conicBrush.Dispose();     } catch { }
            try { _arcMaskBrush.Dispose();   } catch { }

            try { _conicSurface.Dispose();   } catch { }
            try { _arcMaskSurface.Dispose(); } catch { }

            try { _conicRotationProps.Dispose(); } catch { }
            try { _arcRotationProps.Dispose();   } catch { }

            try { _sprite.Dispose();    } catch { }
            try { Container.Dispose();  } catch { }
        }
    }

    // Returns a ContainerVisual sized pxSquare × pxSquare — the same
    // visual-diagonal coverage the shipping stroke bakes its rotating
    // brush into. Inside sits one SpriteVisual filling that container,
    // with a brush chosen by `part`:
    //   - Conic    : SurfaceBrush over the painted conic surface.
    //   - ArcMask  : SurfaceBrush over the painted arc mask surface.
    //   - Combined : CompositionEffectBrush running AlphaMaskEffect on
    //                the two surfaces, identical to the first mask stage
    //                in CreateConicArcStroke but WITHOUT the stroke
    //                silhouette alpha stage — exposes the full pre-clip
    //                arc geometry to the eye.
    //
    // Both brushes spin independently at HuePeriodSeconds /
    // ArcPeriodSeconds around the sprite's geometric centre
    // (pxSquare / 2). If the rotation centre and the brush-painting
    // centre disagree (the working hypothesis behind the top/bottom
    // luminance asymmetry observation), the wobble reads immediately on the naked
    // preview as a drifting lobe or a wandering dead-spot.
    //
    // No effect-pipeline colour knobs (Saturation / Hue / Exposure) —
    // those are state-blend concerns; the diagnostic is about geometry,
    // and stripping the colour pipeline keeps the visual signal focused
    // on where each lobe actually lands.
    //
    // `arcFillColor` — the caller picks a theme-legible opaque colour for
    // the arc mask surface (black on light substrates, white on dark). The
    // Combined path composites through AlphaMaskEffect and reads only the
    // mask's .A, so the colour is invisible there; the ArcMask rail draws
    // the mask directly and relies on this colour to show up against the
    // window's LayerFillColorDefaultBrush in both themes.
    //
    // Returns a NakedPreview bundle the caller MUST hold and Dispose when
    // replaced — see the type-level comment on NakedPreview for why.
    public static NakedPreview CreateNakedMaskPreview(
        Compositor compositor, Vector2 hudSize,
        ConicArcStrokeConfig cfg, NakedMaskPart part,
        Color arcFillColor,
        ProcessingVariant gradeVariant, bool isDark)
    {
        // Reuse the exact pxSquare math from CreateConicArcStroke so the
        // naked preview paints the same brush footprint. Any drift here
        // would invalidate the diagnostic — the user would be looking at
        // a different geometry than the shipping stroke samples.
        var innerSize = new Vector2(hudSize.X - 2f * InsetDip, hudSize.Y - 2f * InsetDip);
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

        // Stretch.Fill — source is pxSquare, sprite is pxSquare, map 1:1.
        // (CreateConicArcStroke uses Stretch.None because its sprite is
        // innerSize, not pxSquare; here the sprite IS pxSquare so Fill
        // lands trivially.)
        var conicBrush = compositor.CreateSurfaceBrush(conicSurface);
        conicBrush.Stretch = CompositionStretch.Fill;

        var arcMaskBrush = compositor.CreateSurfaceBrush(arcMaskSurface);
        arcMaskBrush.Stretch = CompositionStretch.Fill;

        // Rotate around the sprite's OWN centre. The shipping stroke
        // rotates around the visual centre (innerSize/2) because its
        // sprite is innerSize and the surface is oversized pxSquare; here
        // the sprite is pxSquare and the surface is pxSquare, so the
        // correct rotation centre is pxSquare/2. This is the whole point
        // of the diagnostic: if the brush's painted centre doesn't match
        // pxSquare/2, the naked preview will show a wobble the shipping
        // stroke silently masks.
        var centre = new Vector2(pxSquare / 2f, pxSquare / 2f);

        // Capture the returned PropertySets — see NakedPreview class
        // comment. Letting them die on GC leaks two Forever animations
        // per rebuild and eventually freezes every Composition animation
        // in the window.
        var conicRotationProps = StartRotation(
            compositor, conicBrush, centre,
            cfg.HuePeriodSeconds,
            cfg.HueDirection,
            cfg.HuePhaseTurns,
            cfg.HueEaseP1X, cfg.HueEaseP1Y,
            cfg.HueEaseP2X, cfg.HueEaseP2Y,
            cfg.HueMinSpeedFraction);
        var arcRotationProps = StartRotation(
            compositor, arcMaskBrush, centre,
            cfg.ArcPeriodSeconds,
            cfg.ArcDirection,
            cfg.ArcPhaseTurns,
            cfg.ArcEaseP1X, cfg.ArcEaseP1Y,
            cfg.ArcEaseP2X, cfg.ArcEaseP2Y,
            cfg.ArcMinSpeedFraction);

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size = new Vector2(pxSquare, pxSquare);

        CompositionEffectBrush? effectBrush = null;
        switch (part)
        {
            case NakedMaskPart.Conic:
            {
                // Grade the bare cone like the live stroke in the chosen state
                // (Transcribing ⇒ grey, Rewriting ⇒ colour) so the toggle reads
                // the same as the swipe will. Static seed — no state blend here.
                var graded = BuildVariantGrading(
                    new CompositionEffectSourceParameter("Conic"), cfg, gradeVariant, isDark);
                var ef = compositor.CreateEffectFactory(graded);
                effectBrush = ef.CreateBrush();
                effectBrush.SetSourceParameter("Conic", conicBrush);
                sprite.Brush = effectBrush;
                break;
            }
            case NakedMaskPart.ArcMask:
                // Pure alpha mask — Sat/Hue/Exp are a no-op on a white monochrome
                // surface, so the variant toggle leaves it untouched.
                sprite.Brush = arcMaskBrush;
                break;
            case NakedMaskPart.Combined:
            {
                // Conic ⊗ Arc, with the conic graded for the chosen state first —
                // output = (graded.RGB, graded.A · Arc.A). Same masking stage as
                // the shipping stroke, minus the silhouette.
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

    // OklchToRgb + LinearToSrgb moved to Deckle.Composition module
    // 2026-05-02 — see Primitives/ColorSpace.cs. The methods are pure
    // math and stayed in the same namespace, so the call to
    // ColorSpace.OklchToRgb in PaintConicSurface resolves via the
    // cross-assembly project reference.
}

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
    // ║  Conic clone preview (HudPlayground only)                          ║
    // ╚════════════════════════════════════════════════════════════════════╝
    // A SECOND, fully independent DOUBLE comet — its own conic + arc-mask
    // surfaces, brushes and rotations — painted from the SAME
    // ConicArcStrokeConfig as the contour (same OKLCh palette, same arc shape),
    // but placed freely and spun at the CLONE periods. It is the Playground
    // preview of EXACTLY what the pinned reveal shows behind the digits: the contour's
    // visible material (conic ⊗ arc comet), graded per state, posable + timable
    // on its own.
    //
    // Why a clone rather than sampling the contour's own surfaces: to place the
    // cone's apex freely (centre vs corner) AND spin it independently of the
    // contour, it needs its own surfaces + rotations. Placement comes from
    // cfg.CloneCentre*Fraction, speeds from cfg.CloneHue/CloneArcPeriodSeconds —
    // the SAME config the live reveal reads, so the preview and the reveal stay
    // in lock-step.
    //
    // Geometry. The sprite is the row frame (hudSize, e.g. 272×78); both surfaces
    // are a square auto-sized (CoverageSquareSide) so their inscribed circle
    // reaches every corner of the frame from the placed apex (Stretch.None keeps
    // the 1:1 footprint and overhangs the frame). The placement transform lands
    // the cone's centre at the apex and spins it around itself — apex = hudSize/2
    // reproduces the contour's centred cone, (0,0) radiates from the top-left.
    // Because the sprite clips to hudSize, what shows IS exactly the slice the
    // digit row would sample at that placement.
    //
    // Grading. Like the contour, through the Saturation / Hue / Exposure chain,
    // seeded at the chosen variant's resting values (Transcribing ⇒ greyscale,
    // Rewriting ⇒ colour). The arc mask carries the visible motion in greyscale —
    // see the DigitReveal header on why the naked cone alone shows none. Toggling
    // the variant rebuilds the preview (the seed is static — no live blend in a
    // preview).
    //
    // Returns a disposable bundle the caller MUST hold and Dispose before
    // mounting a replacement — Forever-animation-leak hazard (four
    // ScalarKeyFrameAnimations: Linear/Eased × hue/arc). See NakedPreview's
    // class comment for the full why.
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

    // Build a clone cone for the playground. `hudSize` is the row frame the
    // sprite clips to; the cone-centre placement is read from the SHARED config
    // fraction (cfg.CloneCentre*Fraction · hudSize) — the SAME value the live
    // digit reveal reads, so a placement slider drives both in lock-step
    // ((0.5,0.5) = centred, reproducing the contour; (0,0) = top-left).
    // `gradeVariant` + `isDark` pick the resting grading (Transcribing ⇒ grey,
    // Rewriting ⇒ colour) so the preview reads as the digits do in that state.
    public static ConicClonePreview CreateConicClonePreview(
        Compositor compositor, Vector2 hudSize,
        ConicArcStrokeConfig cfg,
        ProcessingVariant gradeVariant, bool isDark,
        bool animationsEnabled = true)
    {
        var coneCentre = new Vector2(
            cfg.CloneCentreXFraction * hudSize.X,
            cfg.CloneCentreYFraction * hudSize.Y);

        // Auto-scale the cone to the placement: its inscribed radius (pxSquare/2)
        // is pinned to the apex's FARTHEST corner of the row frame, so a digit
        // anywhere in the row samples a live hue instead of falling off the
        // painted surface (out-of-bounds = transparent). Apex centred ⇒ radius =
        // half-diagonal (pxSquare = the row diagonal); apex in a corner ⇒ radius
        // = full diagonal (pxSquare doubles). The cone is a pure ANGULAR gradient
        // (hue = angle), invariant under radial scaling, so growing the surface
        // only extends coverage — it never distorts the look. No scale knob: the
        // value is always the tight optimum for the placement. Shared with the
        // live reveal via CoverageSquareSide so the two can't drift.
        int pxSquare = CoverageSquareSide(hudSize, coneCentre);

        var canvasDevice = CanvasDevice.GetSharedDevice();
        EnsureDeviceLostHook(canvasDevice);
        var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(
            compositor, canvasDevice);

        // Same decoupled clone palette as the live reveal (CloneOklch* lightness/
        // chroma) so the preview shows the digits' real surface, not the contour's.
        var conicSurface = PaintConicSurface(canvasDevice, graphicsDevice, pxSquare,
            CloneSurfaceConfig(cfg));
        var arcSurface   = PaintArcMaskSurface(
            canvasDevice, graphicsDevice, pxSquare, cfg, Microsoft.UI.Colors.White);

        // Stretch.None keeps each surface at its oversized pxSquare footprint,
        // centred on the sprite (alignment ratio 0.5) — placed centre at
        // hudSize/2. The placement transform then parks that centre at
        // `coneCentre` and spins around it. The conic and arc spin at the CLONE
        // periods (independent of the contour); both share the apex so the comet
        // stays concentric with the cone.
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

        // Graded cone ⊗ arc comet — output = (graded.RGB, graded.A · Arc.A).
        // Same masking stage as the contour and the live reveal, minus the
        // silhouette. Grading is a static seed for the chosen variant.
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

    // Wraps a conic source in the Saturation → Hue → Exposure chain the contour
    // runs, seeded at the variant's resting values (Transcribing split Dark/Light,
    // Rewriting unified). Shared by the clone and naked previews so a graded
    // preview reads identically to the live stroke in that state — greyscale for
    // Transcribing (Saturation 0), full colour for Rewriting. Static (no
    // PropertySet binding): previews don't blend between states.
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

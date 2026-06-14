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
    // A SECOND, fully independent conic cone — its own surface, brush and
    // placement — painted from the SAME ConicArcStrokeConfig as the contour
    // (same OKLCh palette, same hue-rotation cadence). It mutualises the
    // config values but NOT the physical surface: deliberately decoupled from
    // the live contour's background cone, so the developer can park its apex
    // anywhere and watch how the cone falls across the row.
    //
    // Why a clone rather than the shared-surface route the digit reveal uses:
    // the digit reveal places its brush from the live stroke's surface +
    // rotation PropertySet, which ties its centre to the contour's centre. To
    // experiment with a DIFFERENT placement (centre vs top-left) ahead of a
    // new swipe, we want a cone whose centre is a free knob — hence its own
    // surface and its own rotation, parametrised by `coneCentre`.
    //
    // Geometry. The sprite is the row frame (hudSize, e.g. 272×78); the conic
    // surface is a square auto-sized so its inscribed circle reaches every corner
    // of the frame from the placed apex (Stretch.None keeps its 1:1 footprint and
    // overhangs the frame — see the pxSquare comment below). The placement
    // transform lands the cone's centre at `coneCentre` within that frame and
    // spins it around itself — coneCentre = hudSize/2 reproduces the contour's
    // centred cone, coneCentre = (0,0) radiates it from the top-left corner.
    // Because the sprite clips to hudSize, what shows IS exactly the slice the
    // digit row would sample at that placement.
    //
    // Grading. Unlike the naked Conic diagnostic, the clone IS graded — through
    // the same Saturation / Hue / Exposure chain the contour runs, seeded at the
    // chosen variant's resting values (Transcribing ⇒ greyscale, Rewriting ⇒
    // colour). That mirrors what the swipe will actually reveal in each state, so
    // the developer judges the real look while placing the apex. Toggling the
    // variant rebuilds the preview (the seed is static — no live blend needed in
    // a preview).
    //
    // Returns a disposable bundle the caller MUST hold and Dispose before
    // mounting a replacement — same Forever-animation-leak hazard as
    // NakedPreview (two ScalarKeyFrameAnimations live on the compositor until
    // explicitly stopped). See NakedPreview's class comment for the full why.
    public sealed class ConicClonePreview : IDisposable
    {
        public ContainerVisual Container { get; }

        private readonly SpriteVisual _sprite;
        private readonly CompositionSurfaceBrush _conicBrush;
        private readonly CompositionEffectBrush _effectBrush;
        private readonly CompositionDrawingSurface _conicSurface;
        private readonly CompositionPropertySet _rotationProps;

        private bool _disposed;

        internal ConicClonePreview(
            ContainerVisual container,
            SpriteVisual sprite,
            CompositionSurfaceBrush conicBrush,
            CompositionEffectBrush effectBrush,
            CompositionDrawingSurface conicSurface,
            CompositionPropertySet rotationProps)
        {
            Container      = container;
            _sprite        = sprite;
            _conicBrush    = conicBrush;
            _effectBrush   = effectBrush;
            _conicSurface  = conicSurface;
            _rotationProps = rotationProps;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop the TransformMatrix expression first so stopping the
            // Linear / Eased scalars it reads can't race a live evaluation.
            try { _conicBrush.StopAnimation("TransformMatrix"); } catch { }
            try { _rotationProps.StopAnimation("Linear"); } catch { }
            try { _rotationProps.StopAnimation("Eased");  } catch { }

            try { _effectBrush.Dispose();   } catch { }
            try { _conicBrush.Dispose();    } catch { }
            try { _conicSurface.Dispose();  } catch { }
            try { _rotationProps.Dispose(); } catch { }
            try { _sprite.Dispose();        } catch { }
            try { Container.Dispose();      } catch { }
        }
    }

    // Build a clone cone for the playground. `hudSize` is the row frame the
    // sprite clips to; `coneCentre` is the cone-centre placement within that
    // frame (hudSize/2 = centred, reproducing the contour; (0,0) = top-left).
    // `gradeVariant` + `isDark` pick the resting grading (Transcribing ⇒ grey,
    // Rewriting ⇒ colour) so the preview reads as the swipe would in that state.
    public static ConicClonePreview CreateConicClonePreview(
        Compositor compositor, Vector2 hudSize,
        ConicArcStrokeConfig cfg, Vector2 coneCentre,
        ProcessingVariant gradeVariant, bool isDark)
    {
        // Auto-scale the cone to the placement: its inscribed radius (pxSquare/2)
        // is pinned to the apex's FARTHEST corner of the row frame, so a digit
        // anywhere in the row samples a live hue instead of falling off the
        // painted surface (out-of-bounds = transparent). Apex centred ⇒ radius =
        // half-diagonal (pxSquare = the row diagonal, the un-scaled value); apex
        // in a corner ⇒ radius = full diagonal (pxSquare doubles). The cone is a
        // pure ANGULAR gradient (hue = angle), invariant under radial scaling, so
        // growing the surface only extends coverage — it never distorts the look.
        // No scale knob: the value is always the tight optimum for the placement.
        // The inscribed circle is rotation-invariant, so this also guarantees
        // coverage at every spin angle.
        var corners = new[]
        {
            new Vector2(0f, 0f),        new Vector2(hudSize.X, 0f),
            new Vector2(0f, hudSize.Y), new Vector2(hudSize.X, hudSize.Y),
        };
        float maxCornerDist = 0f;
        foreach (var corner in corners)
            maxCornerDist = MathF.Max(maxCornerDist, Vector2.Distance(coneCentre, corner));
        int pxSquare = Math.Max(1, (int)Math.Ceiling(2.0 * maxCornerDist));

        var canvasDevice = CanvasDevice.GetSharedDevice();
        EnsureDeviceLostHook(canvasDevice);
        var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(
            compositor, canvasDevice);

        var conicSurface = PaintConicSurface(canvasDevice, graphicsDevice, pxSquare, cfg);

        // Stretch.None keeps the surface at its oversized pxSquare footprint,
        // centred on the sprite (alignment ratio 0.5) — placed centre at
        // hudSize/2. The placement transform then parks that centre at
        // `coneCentre` and spins around it.
        var conicBrush = compositor.CreateSurfaceBrush(conicSurface);
        conicBrush.Stretch = CompositionStretch.None;

        var rotationProps = StartRotation(
            compositor, conicBrush, hudSize / 2f,
            cfg.HuePeriodSeconds,
            cfg.HueDirection,
            cfg.HuePhaseTurns,
            cfg.HueEaseP1X, cfg.HueEaseP1Y,
            cfg.HueEaseP2X, cfg.HueEaseP2Y,
            cfg.HueMinSpeedFraction,
            placement: coneCentre);

        // Grade the cone exactly like the contour at its resting values for the
        // chosen variant. Static seed — a preview has no state blend to animate.
        var gradedGraph = BuildVariantGrading(
            new CompositionEffectSourceParameter("Conic"), cfg, gradeVariant, isDark);
        var effectFactory = compositor.CreateEffectFactory(gradedGraph);
        var effectBrush = effectFactory.CreateBrush();
        effectBrush.SetSourceParameter("Conic", conicBrush);

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size  = hudSize;          // clips the cone to the row frame
        sprite.Brush = effectBrush;

        var container = compositor.CreateContainerVisual();
        container.Size = hudSize;
        container.Children.InsertAtTop(sprite);

        return new ConicClonePreview(container, sprite, conicBrush, effectBrush, conicSurface, rotationProps);
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

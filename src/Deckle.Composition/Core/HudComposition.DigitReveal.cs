using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;

namespace Deckle.Composition;

public static partial class HudComposition
{
    // ── Digit reveal — a chrono digit as glass onto a CLONE of the contour ───
    //
    // A digit's glyph is the opacity mask carved into a CLONE of the contour's
    // visible material: the DOUBLE comet (conic ⊗ arc mask), graded grey in
    // Transcribing / colour in Rewriting. The digit reads as glass onto that
    // rotating comet. Built as Conic→Sat→Hue→Exp→AlphaMask(Arc)→AlphaMask(Glyph)
    // — the contour's pipeline (Factories.cs) with the glyph swapped in for the
    // rounded-rect silhouette.
    //
    // What is shared vs cloned:
    //   • GRADING is shared — the three Sat/Hue/Exp scalars bind to the stroke's
    //     EffectProps, so the reveal greys/colours in lock-step with the
    //     contour's state blend (the brush-combining table forbids feeding the
    //     stroke's built effect brush into another effect, so we share one level
    //     down at the PropertySet and rebuild the graph).
    //   • SURFACES + ROTATION are cloned — own conic + arc surfaces (auto-scaled,
    //     placed) and own hue + arc rotation PropertySets at the CLONE periods,
    //     independent of the contour, so the reveal animates at its own pace. The
    //     two rotations are SHARED across the six digits (one RevealConeMaterial)
    //     so they spin as one coherent cone, in phase.
    //
    // Why a DOUBLE comet, not the naked cone: in Transcribing the cone is
    // desaturated, and OKLCh is luminance-uniform by construction — a greyscale
    // cone is a flat field whose rotation is invisible. Only the swept comet
    // SHAPE (alpha, not colour) reads. So the arc mask isn't decoration, it's
    // what makes the reveal legible in greyscale at all.
    //
    // Why a clone surface rather than the contour's own (the earlier "one
    // physical conic behind the whole HUD" route): that route forced a single
    // host-sized surface centred on the contour, clipping digits off its coverage
    // at some placements — the "no digit, just the two white dots" symptom. The
    // clone is auto-scaled to reach every host corner from a config-driven apex
    // (CloneCentre*Fraction), so coverage is guaranteed AND the apex is a live
    // knob — selectable as the ConicClone Playground target.
    //
    // The glyph mask is NOT painted by Win2D. It is the alpha channel of the
    // chrono's own readout TextBlock, captured via TextBlock.GetAlphaMask()
    // (Microsoft.UI.Xaml.Controls) and fed to a CompositionMaskBrush as its
    // Mask. This is deliberate: the readout already renders the right Bitcount
    // face, upright, at the right size and metrics — reusing its rasterised
    // alpha guarantees the masked digit is pixel-identical to the readout, and
    // sidesteps the entire Win2D variable-font matching problem (the font file
    // exposes one typographic family carrying both Normal and Oblique faces;
    // DirectWrite's file:// loader in Win2D resolved that typographic name to
    // the variable font's default — slanted — instance, which is why the
    // earlier Win2D-painted mask came out italic. XAML uses the font's WWS
    // legacy family name and gets the upright face; rather than fight the
    // matching, we just borrow XAML's already-correct rasterisation).
    //
    // The glyph is masked INSIDE the effect graph via AlphaMaskEffect — the
    // SAME mechanism the stroke (arc + silhouette masks) and the naked Combined
    // preview use, and the only one proven to composite here. An earlier
    // variant pulled the built EffectBrush out and fed it to a
    // CompositionMaskBrush.Source: that rendered transparent (a fully-built
    // effect brush handed back as a MaskBrush source does not composite),
    // which is why the revealed digits came up empty — the primary glyph faded
    // out under the swipe and the reveal above it drew nothing, leaving the
    // bare Stop-tone background. Keeping the mask in the graph, as a surface
    // alpha source, is what works.
    //
    // The reveal is driven managed, exactly like the flat-accent overlay it
    // sits on top of: SetHeat pushes the swipe wave's per-digit heat onto the
    // sprite Opacity, so the conic-filled glyph cross-fades in over the
    // (identically-shaped) primary glyph beneath it. Keeping the existing
    // driver means the MOTION is unchanged from today — only the revealed
    // material differs (living conic vs flat accent).
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

        // Push the swipe wave's heat [0,1] onto the sprite Opacity. Rounded to
        // 3 decimals so floating noise (0.9999997) doesn't re-invalidate the
        // render pass every vsync — same guard the flat-overlay path uses.
        public void SetHeat(float heat)
        {
            double rounded = Math.Round(Math.Clamp(heat, 0f, 1f), 3);
            if (Visual.Opacity != rounded)
                Visual.Opacity = (float)rounded;
        }

        // Stops both surface brushes' TransformMatrix (they bind the shared
        // clone hue/arc rotations) + the three grading expressions, then
        // disposes this reveal's OWN brushes + sprite. The clone SURFACES and
        // the shared hue/arc rotation PropertySets are owned by the caller
        // (HudChrono's RevealConeMaterial) and disposed in TearDownReveals AFTER
        // every reveal — never here, or a still-living brush would sample freed
        // memory. The grading EffectProps belong to the ProcessingStroke. The
        // glyph alpha-mask brush belongs to the XAML TextBlock
        // (TextBlock.GetAlphaMask). Reveal teardown (StopSwipe) always runs
        // before the stroke's own Dispose (DetachProcessingVisual) and before
        // the material's, so the ordering is safe.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _effectBrush.StopAnimation("Sat.Saturation"); } catch { }
            try { _effectBrush.StopAnimation("Hue.Angle");      } catch { }
            try { _effectBrush.StopAnimation("Exp.Exposure");   } catch { }
            try { _conicBrush.StopAnimation("TransformMatrix"); } catch { }
            try { _arcBrush.StopAnimation("TransformMatrix");   } catch { }

            try { _effectBrush.Dispose(); } catch { }
            try { _conicBrush.Dispose();  } catch { }
            try { _arcBrush.Dispose();    } catch { }
            try { Visual.Dispose(); } catch { }
        }
    }

    // Build one digit's reveal: its glyph as a window onto the shared clone
    // cone material. Same DOUBLE-comet pipeline as the contour (Conic ⊗ arc),
    // graded live via the stroke's EffectProps, but masked by the digit glyph
    // instead of the rounded-rect silhouette — so the chrono digit reads as
    // glass onto the rotating comet. See the class header for the why.
    //
    // `material`                 — the shared clone surfaces + rotations
    //                              (BuildRevealConeMaterial), built once and
    //                              shared across all six digits so they read ONE
    //                              cone in phase. Owned + disposed by the caller.
    // `glyphAlphaMask`           — the digit's rasterised alpha (TextBlock.Get-
    //                              AlphaMask); the final mask, so the visible
    //                              shape is exactly the readout glyph.
    // `spriteSize`               — the digit cell's size; the sprite covers it.
    // `conicCentreInSpriteSpace` — where the clone surfaces' centre lands in the
    //                              sprite's local space = apex − cellOffset
    //                              (apex = CloneCentre*Fraction · hostSize). Both
    //                              the conic and arc brushes are placed there, so
    //                              the comet stays concentric and every digit
    //                              samples its own slice of ONE cone at the apex.
    //                              (Derivation: Stretch.None centres the surface
    //                              on the sprite — alignment 0.5 — so
    //                              T(−spriteSize/2)·R·T(conicCentre) reduces to
    //                              R(s − surfaceCentre) + apex once the cell
    //                              offset is added back.)
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

        // ── Conic + arc brushes over the shared clone surfaces ───────────────
        // Stretch.None keeps each 1:1; BindPlacedRotation then places + spins
        // each around the apex (conicCentre in sprite space), reading the shared
        // hue/arc rotations — so all six cells form ONE cone, in phase, and the
        // comet mask stays concentric with the cone.
        var conicBrush = compositor.CreateSurfaceBrush(material.ConicSurface);
        conicBrush.Stretch = CompositionStretch.None;
        BindPlacedRotation(compositor, conicBrush, material.HueProps,
            negHalf, conicCentreInSpriteSpace, cfg.HueMinSpeedFraction);

        var arcBrush = compositor.CreateSurfaceBrush(material.ArcSurface);
        arcBrush.Stretch = CompositionStretch.None;
        BindPlacedRotation(compositor, arcBrush, material.ArcProps,
            negHalf, conicCentreInSpriteSpace, cfg.ArcMinSpeedFraction);

        // ── Conic ─► Sat ─► Hue ─► Exp ─► AlphaMask(Arc) ─► AlphaMask(Glyph) ──
        // Same grading chain + arc masking as the contour (Factories.cs); all
        // three grading scalars bind to the SAME stroke.EffectProps the contour
        // binds, so the reveal greys in Transcribing / colours in Rewriting in
        // lock-step with the contour's blend. The arc mask carves the double
        // comet (what makes the reveal visible at all in greyscale, where the
        // luminance-uniform cone alone shows no motion), and the glyph mask is
        // the last node — AlphaMaskEffect output = (Source.RGB, Source.A·Mask.A):
        // the comet where the glyph is opaque, transparent elsewhere.
        var saturationEffect = new SaturationEffect
        {
            Name       = "Sat",
            Saturation = cfg.TranscribingSaturationDark,
            Source     = new CompositionEffectSourceParameter("Conic"),
        };
        var hueEffect = new HueRotationEffect
        {
            Name   = "Hue",
            Angle  = cfg.TranscribingHueShiftTurns * MathF.Tau,
            Source = saturationEffect,
        };
        var exposureEffect = new ExposureEffect
        {
            Name     = "Exp",
            Exposure = cfg.TranscribingExposureDark,
            Source   = hueEffect,
        };
        var arcMaskedGraph = new AlphaMaskEffect
        {
            Source    = exposureEffect,
            AlphaMask = new CompositionEffectSourceParameter("Arc"),
        };
        var glyphMaskedGraph = new AlphaMaskEffect
        {
            Source    = arcMaskedGraph,
            AlphaMask = new CompositionEffectSourceParameter("Glyph"),
        };

        var effectFactory = compositor.CreateEffectFactory(
            glyphMaskedGraph,
            new[] { "Sat.Saturation", "Hue.Angle", "Exp.Exposure" });
        var effectBrush = effectFactory.CreateBrush();
        effectBrush.SetSourceParameter("Conic", conicBrush);
        effectBrush.SetSourceParameter("Arc",   arcBrush);
        effectBrush.SetSourceParameter("Glyph", glyphAlphaMask);

        BindEffectProperty(compositor, effectBrush, "Sat.Saturation", stroke.EffectProps, "Saturation");
        BindEffectProperty(compositor, effectBrush, "Hue.Angle",      stroke.EffectProps, "HueAngle");
        BindEffectProperty(compositor, effectBrush, "Exp.Exposure",   stroke.EffectProps, "Exposure");

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size    = spriteSize;
        sprite.Brush   = effectBrush;
        sprite.Opacity = 0f;   // revealed by SetHeat as the swipe head passes

        return new DigitRevealVisual(sprite, conicBrush, arcBrush, effectBrush);
    }

    // The shared "clone cone material" the six digit reveals sample — built ONCE
    // per swipe, owned by the caller (HudChrono), passed to every CreateDigitReveal.
    //
    // It bundles a DOUBLE-comet's worth of shared state: the auto-scaled conic
    // surface, the auto-scaled arc-mask surface, and the two rotation
    // PropertySets (hue + arc) at the CLONE periods. Two surfaces + two shared
    // rotations is what makes the reveal a clone of the visible CONTOUR (conic ⊗
    // arc comet), not the naked cone — and the rotations are shared across the
    // six cells so they spin as ONE coherent cone, in phase. Independent from the
    // contour's own rotation, so the reveal animates at its own pace.
    //
    // Disposed by the caller AFTER every reveal that samples it (TearDownReveals):
    // the per-cell brushes bind these surfaces + props, so they must outlive the
    // brushes, or a render tick samples freed memory.
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

            // Stop the shared rotations' Forever Linear/Eased anims, then free
            // the props + surfaces. Every per-cell brush that referenced these
            // has already been disposed by the caller (reveals first), so no
            // live expression reads a freed PropertySet here.
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

    // Build the shared clone-cone material for one swipe: paint both surfaces
    // (conic + arc mask), auto-scaled (CoverageSquareSide) so their inscribed
    // circle reaches every corner of the host frame from `apex` — a digit
    // anywhere in the row then samples a painted pixel, never off-surface (the
    // coverage guarantee). Both surfaces share the SAME pxSquare so the comet
    // mask stays concentric with the cone. The two rotation PropertySets spin at
    // the CLONE periods (cfg.CloneHue*/CloneArc*), independent of the contour.
    //
    // arcFill = white: the reveal graph reads only the arc mask's alpha, so its
    // RGB is invisible — same convention as the contour's arc surface.
    public static RevealConeMaterial BuildRevealConeMaterial(
        Compositor compositor, Vector2 hostSize, Vector2 apex, ConicArcStrokeConfig cfg)
    {
        int pxSquare = CoverageSquareSide(hostSize, apex);

        var canvasDevice = CanvasDevice.GetSharedDevice();
        EnsureDeviceLostHook(canvasDevice);
        var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(
            compositor, canvasDevice);

        var conicSurface = PaintConicSurface  (canvasDevice, graphicsDevice, pxSquare, cfg);
        var arcSurface   = PaintArcMaskSurface(canvasDevice, graphicsDevice, pxSquare, cfg,
                                               Microsoft.UI.Colors.White);

        // Clone rotations — own periods/directions, contour's ease/phase. Shared
        // across the six cells (created once here) so the digits stay in phase.
        var hueProps = CreateRotationPropertySet(
            compositor, cfg.CloneHuePeriodSeconds, cfg.CloneHueDirection, cfg.HuePhaseTurns,
            cfg.HueEaseP1X, cfg.HueEaseP1Y, cfg.HueEaseP2X, cfg.HueEaseP2Y);
        var arcProps = CreateRotationPropertySet(
            compositor, cfg.CloneArcPeriodSeconds, cfg.CloneArcDirection, cfg.ArcPhaseTurns,
            cfg.ArcEaseP1X, cfg.ArcEaseP1Y, cfg.ArcEaseP2X, cfg.ArcEaseP2Y);

        return new RevealConeMaterial(conicSurface, arcSurface, hueProps, arcProps);
    }

    // Side of the square conic surface whose inscribed circle reaches every
    // corner of `frame` from `centre` — the coverage-guarantee auto-scale shared
    // by the live digit reveal and the ConicClone preview, so the two never
    // drift. centre at the frame middle ⇒ side = the frame diagonal; centre in a
    // corner ⇒ side doubles. The inscribed circle is rotation-invariant, so the
    // guarantee holds at every spin angle.
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

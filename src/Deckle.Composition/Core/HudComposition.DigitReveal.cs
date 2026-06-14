using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;

namespace Deckle.Composition;

public static partial class HudComposition
{
    // ── Digit reveal — a chrono digit as a window on the live conic ──────────
    //
    // F1 prototype ("one conic behind the whole HUD"): a digit's glyph becomes
    // the opacity mask that cuts the SAME rotating, colour-graded conic the
    // processing stroke samples. The stroke owns the material (surface +
    // rotation PropertySet + grading PropertySet, exposed via the accessors on
    // ProcessingStroke); this builds a SEPARATE Conic→Sat→Hue→Exp effect brush
    // from those shared objects, then masks it with the digit glyph — so digit
    // and contour breathe in lock-step without the brush-combining table's
    // "EffectBrush can't feed another EffectBrush" limitation (we share one
    // level down, at the surface + PropertySets).
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
        private readonly CompositionEffectBrush  _effectBrush;
        private readonly bool _conicRotates;
        private bool _disposed;

        internal DigitRevealVisual(
            SpriteVisual visual,
            CompositionSurfaceBrush conicBrush,
            CompositionEffectBrush  effectBrush,
            bool conicRotates)
        {
            Visual        = visual;
            _conicBrush   = conicBrush;
            _effectBrush  = effectBrush;
            _conicRotates = conicRotates;
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

        // Tears down ONLY what this reveal owns. The conic surface, EffectProps
        // and HueRotationProps belong to the ProcessingStroke and are disposed
        // by it — never here, or a still-living stroke would sample freed
        // memory. The glyph alpha-mask brush belongs to the XAML TextBlock
        // (TextBlock.GetAlphaMask) — XAML owns its lifetime, so we never
        // dispose it either. Reveal teardown (StopSwipe) always runs before the
        // stroke's own Dispose (DetachProcessingVisual), so the ordering is
        // safe.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _effectBrush.StopAnimation("Sat.Saturation"); } catch { }
            try { _effectBrush.StopAnimation("Hue.Angle");      } catch { }
            try { _effectBrush.StopAnimation("Exp.Exposure");   } catch { }
            if (_conicRotates)
                try { _conicBrush.StopAnimation("TransformMatrix"); } catch { }

            try { _effectBrush.Dispose(); } catch { }
            try { _conicBrush.Dispose();  } catch { }
            try { Visual.Dispose(); } catch { }
        }
    }

    // Build a digit reveal sharing `stroke`'s living conic material.
    //
    // `glyphAlphaMask`           — the digit's rasterised alpha, from
    //                              TextBlock.GetAlphaMask(); used as the
    //                              CompositionMaskBrush mask, so the visible
    //                              shape is exactly the readout glyph.
    // `spriteSize`               — the digit cell's size; the sprite covers it,
    //                              and the alpha mask (which carries the
    //                              TextBlock's own alignment/stretch) lands the
    //                              glyph in place within it.
    // `conicCentreInSpriteSpace` — where the conic surface centre lands in the
    //                              sprite's local space. = hostCentre − cellOffset,
    //                              so a surface pixel maps to the SAME HUD point
    //                              the stroke shows there regardless of which
    //                              cell paints it. This is what makes it ONE
    //                              conic across the HUD (F1) rather than a
    //                              private conic per digit. (Derivation: with
    //                              Stretch.None the surface is centred on the
    //                              sprite — alignment ratio 0.5 — so the
    //                              T(−spriteSize/2)·R·T(conicCentre) matrix
    //                              reduces to R(s − surfaceCentre) + conicCentre,
    //                              identical to the stroke's own
    //                              R(s − surfaceCentre) + hostCentre once the
    //                              cell offset is added back.)
    public static DigitRevealVisual CreateDigitReveal(
        Compositor compositor,
        ProcessingStroke stroke,
        CompositionBrush glyphAlphaMask,
        Vector2 spriteSize,
        Vector2 conicCentreInSpriteSpace)
    {
        // ── Conic brush over the SHARED surface ──────────────────────────────
        // Stretch.None keeps the conic 1:1 (no rescale), like the stroke; the
        // TransformMatrix then places + rotates it so it lines up in HUD space.
        var conicBrush = compositor.CreateSurfaceBrush(stroke.ConicSurface);
        conicBrush.Stretch = CompositionStretch.None;

        var negHalf = -spriteSize / 2f;
        float minFrac = Math.Clamp(stroke.Config.HueMinSpeedFraction, 0f, 1f);
        bool conicRotates = stroke.HueRotationProps is not null;
        if (conicRotates)
        {
            // SAME expression as StartRotation, only the translation vectors
            // differ — negHalf instead of −visualCentre, conicCentre instead of
            // +visualCentre. Same `props` + `minFrac` ⇒ perfectly in phase with
            // the stroke's own rotation.
            var expr = compositor.CreateExpressionAnimation(
                "Matrix3x2.CreateTranslation(negHalf) * " +
                "Matrix3x2.CreateRotation(props.Linear * minFrac + props.Eased * (1.0 - minFrac)) * " +
                "Matrix3x2.CreateTranslation(conicCentre)");
            expr.SetReferenceParameter("props", stroke.HueRotationProps);
            expr.SetVector2Parameter("negHalf", negHalf);
            expr.SetVector2Parameter("conicCentre", conicCentreInSpriteSpace);
            expr.SetScalarParameter("minFrac", minFrac);
            conicBrush.StartAnimation("TransformMatrix", expr);
        }
        else
        {
            // Frozen-rotation stroke (Recording-only path): static placement at
            // the phase offset. The swipe never runs in Recording, so this
            // branch is effectively unreachable in shipping — kept for safety.
            conicBrush.TransformMatrix =
                Matrix3x2.CreateTranslation(negHalf) *
                Matrix3x2.CreateRotation(MathF.Tau * stroke.Config.HuePhaseTurns) *
                Matrix3x2.CreateTranslation(conicCentreInSpriteSpace);
        }

        // ── Effect graph: Conic ─► Sat ─► Hue ─► Exp ─► AlphaMask(Glyph) ─────
        // Identical grading chain to the contour (Factories.cs), and all three
        // scalars bind to the SAME stroke.EffectProps the contour binds — so the
        // digit shows the EXACT same cone the contour shows, in the same grading,
        // in lock-step (during Transcribing that means desaturated, per
        // TranscribingSaturation; we do NOT recolour the digit — it is "le même
        // cône que le contour", nothing more). The glyph mask is the last node —
        // same in-graph masking the stroke and naked Combined preview use;
        // AlphaMaskEffect output = (Source.RGB, Source.A · Mask.A): the cone where
        // the glyph is opaque, transparent elsewhere.
        var cfg = stroke.Config;
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

        var maskedGraph = new AlphaMaskEffect
        {
            Source    = exposureEffect,
            AlphaMask = new CompositionEffectSourceParameter("Glyph"),
        };

        var effectFactory = compositor.CreateEffectFactory(
            maskedGraph,
            new[] { "Sat.Saturation", "Hue.Angle", "Exp.Exposure" });
        var effectBrush = effectFactory.CreateBrush();
        effectBrush.SetSourceParameter("Conic", conicBrush);
        effectBrush.SetSourceParameter("Glyph", glyphAlphaMask);

        BindEffectProperty(compositor, effectBrush, "Sat.Saturation", stroke.EffectProps, "Saturation");
        BindEffectProperty(compositor, effectBrush, "Hue.Angle",      stroke.EffectProps, "HueAngle");
        BindEffectProperty(compositor, effectBrush, "Exp.Exposure",   stroke.EffectProps, "Exposure");

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size    = spriteSize;
        sprite.Brush   = effectBrush;
        sprite.Opacity = 0f;   // revealed by SetHeat as the swipe head passes

        return new DigitRevealVisual(sprite, conicBrush, effectBrush, conicRotates);
    }
}

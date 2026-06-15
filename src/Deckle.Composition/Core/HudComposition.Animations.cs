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

    // Forwards an effect-brush property to a named PropertySet scalar via
    // a trivial ExpressionAnimation — "whatever the scalar is, the effect
    // property reads the same". Separates "what's animated" (the scalar,
    // driven by ApplyVariant) from "what consumes the value" (the effect
    // graph). Needed because Composition KeyFrameAnimations on the effect
    // brush itself don't support reading "this.CurrentValue" reliably when
    // another animation is in flight on the same property.
    private static void BindEffectProperty(
        Compositor compositor,
        CompositionEffectBrush effectBrush,
        string effectPropertyPath,
        CompositionPropertySet source,
        string sourcePropertyName)
    {
        var expr = compositor.CreateExpressionAnimation($"src.{sourcePropertyName}");
        expr.SetReferenceParameter("src", source);
        effectBrush.StartAnimation(effectPropertyPath, expr);
    }

    // ── Cyclic rotation with out-in easing + vestigial speed floor ─────
    //
    // What it does (UX):
    //   The rotations breathe — fast at the cycle seam, gentle slow-down
    //   around the midpoint, fast again at the next seam — via a
    //   cubic-bezier shaped as an OUT-IN curve (0.125, 0.375, 0.875, 0.625).
    //
    //   The defining property: both endpoint tangents have slope 3.0
    //   (= 0.375/0.125 = (1-0.625)/(1-0.875)). When the KeyFrame animation
    //   loops via AnimationIterationBehavior.Forever, the seam between
    //   cycles is C¹-continuous: the eye reads the motion as one
    //   continuous pulse repeating, not a start-stop-restart.
    //
    //   Contrast with the earlier in-out (0.2, 0, 0.8, 1): zero-slope
    //   endpoints meant every cycle ended at near-zero angular velocity,
    //   and the next cycle started at zero again. Two plateaus adjacent
    //   in time read as "the animation broke". That forced a linear-blend
    //   workaround (minSpeedFraction) to lift the floor, which itself
    //   compressed the peak toward the mean — pulse lost either way.
    //
    //   Out-in solves this structurally: no plateaus at the seam, so the
    //   pulse shape (slow → fast → slow → fast) is preserved intact.
    //
    // Velocity profile (at ω_mean = 2π/8s ≈ 45°/s, minSpeedFraction = 0):
    //   At t = 0, 1      ω ≈ 3 · ω_mean ≈ 135°/s  (peak at the seam)
    //   At t ≈ 0.5       ω ≈ 0.5 · ω_mean ≈ 22°/s (mid-cycle slow-down)
    //   Angular velocity never hits zero.
    //
    // `minSpeedFraction` (f) — vestigial with the current out-in curve
    // but kept for playground experimentation with exotic ease shapes.
    // Blends a pure linear scalar with the eased one:
    //   angle(t) = Linear · f + Eased · (1 − f)
    //   ω(t)     = ω_mean · [ f + (1 − f)·E'(t) ]
    // At f = 0 (shipping default) the eased curve is used as-is.
    //
    // How it works (implementation):
    //   Two scalars on the same PropertySet animate over the period —
    //   `Linear` (no easing function = linear interpolation) and `Eased`
    //   (cubic-bezier). An ExpressionAnimation rebuilds the Matrix3x2
    //   every frame around the visual centre with the angle computed as:
    //
    //       angle = Linear · f + Eased · (1 − f)
    //
    //   Both scalars start at startAngle and end at endAngle over the
    //   same period, so the sum is in [startAngle, endAngle] at all
    //   times and lands exactly on endAngle at period end — the loop
    //   closes seamlessly with no phase drift across iterations.
    // Returns the internal PropertySet so the caller (typically
    // CreateConicArcStroke) can hold a strong ref and explicitly
    // StopAnimation / Dispose at teardown. Letting the managed ref
    // die leaks the two ScalarKeyFrameAnimations (Linear / Eased)
    // indefinitely on the compositor — after enough rebuilds the
    // compositor saturates and Forever animations stop firing.
    // `placement` (optional) — where the surface's placed centre lands after the
    // rotation, in the brush's visual space. The composite is
    // T(−visualCentre)·R(θ)·T(placement): the surface still spins around its own
    // centre, but that centre is parked at `placement` instead of back at
    // `visualCentre`. Null ⇒ placement = visualCentre, i.e. rotate-in-place (the
    // shipping contour / naked path — byte-identical to before this param existed).
    // The conic-clone preview passes a free placement so the developer can drag the
    // cone's apex anywhere in the row frame.
    private static CompositionPropertySet StartRotation(
        Compositor compositor,
        CompositionSurfaceBrush brush,
        Vector2 visualCentre,
        double periodSeconds,
        float direction,
        float phaseTurns,
        float easeP1X, float easeP1Y,
        float easeP2X, float easeP2Y,
        float minSpeedFraction,
        Vector2? placement = null)
    {
        var props = CreateRotationPropertySet(
            compositor, periodSeconds, direction, phaseTurns,
            easeP1X, easeP1Y, easeP2X, easeP2Y);
        BindPlacedRotation(
            compositor, brush, props,
            -visualCentre, placement ?? visualCentre, minSpeedFraction);
        return props;
    }

    // Create the rotating Linear/Eased PropertySet — the angular SOURCE a
    // brush's TransformMatrix expression reads — WITHOUT binding any brush.
    // Split out of StartRotation so ONE rotation can be SHARED across several
    // brushes (the six digit reveals read one cone in phase: create the props
    // once, then BindPlacedRotation each cell's brush to it). The two Forever
    // ScalarKeyFrameAnimations (Linear/Eased) live on the compositor until
    // explicitly stopped — the caller owns the returned PropertySet and must
    // StopAnimation("Linear"/"Eased") + Dispose it at teardown, or they leak.
    private static CompositionPropertySet CreateRotationPropertySet(
        Compositor compositor,
        double periodSeconds,
        float direction,
        float phaseTurns,
        float easeP1X, float easeP1Y,
        float easeP2X, float easeP2Y)
    {
        float startAngle = MathF.Tau * phaseTurns;
        float fullAngle  = MathF.Tau * direction;
        float endAngle   = startAngle + fullAngle;

        var props = compositor.CreatePropertySet();
        props.InsertScalar("Linear", startAngle);
        props.InsertScalar("Eased",  startAngle);

        // Clamp period to a strictly positive minimum. A zero-duration
        // KeyFrameAnimation with IterationBehavior.Forever resolves to
        // end-state on frame 0 and then never advances — visually
        // indistinguishable from "the stroke froze". The shipping
        // defaults are 8s so this clamp is a no-op there; the
        // HudPlayground sliders can now land on 0 without killing the
        // animation silently. 0.05s ≈ 20 turns/second, the fastest
        // readable rotation before the eye sees strobing.
        double clampedPeriod = Math.Max(0.05, periodSeconds);
        var duration = TimeSpan.FromSeconds(clampedPeriod);

        // Linear scalar — no easing function = default linear interpolation
        // between keyframes. Constant angular velocity = 2π / period.
        var linearAnim = compositor.CreateScalarKeyFrameAnimation();
        linearAnim.InsertKeyFrame(0f, startAngle);
        linearAnim.InsertKeyFrame(1f, endAngle);
        linearAnim.Duration          = duration;
        linearAnim.IterationBehavior = AnimationIterationBehavior.Forever;
        props.StartAnimation("Linear", linearAnim);

        // Eased scalar — cubic-bezier easing. Velocity may be near zero
        // around the curve plateaus; the linear scalar carries the floor.
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(easeP1X, easeP1Y),
            new Vector2(easeP2X, easeP2Y));
        var easedAnim = compositor.CreateScalarKeyFrameAnimation();
        easedAnim.InsertKeyFrame(0f, startAngle);
        easedAnim.InsertKeyFrame(1f, endAngle, easing);
        easedAnim.Duration          = duration;
        easedAnim.IterationBehavior = AnimationIterationBehavior.Forever;
        props.StartAnimation("Eased", easedAnim);

        return props;
    }

    // Bind a brush's TransformMatrix to a rotation PropertySet: the surface
    // spins via T(neg)·R(θ)·T(pos), reading the props' blended Linear/Eased
    // angle. `neg` = −(rotation pivot in brush space), `pos` = where that pivot
    // lands after rotation (= pivot ⇒ rotate in place; = elsewhere ⇒ park the
    // spinning centre there). `minSpeedFraction` (f) mixes the two scalars:
    // pure easing at f=0, pure linear at f=1, clamped so a stray value can't
    // invert (f<0) or over-amplify (f>1) the rotation. Shared by StartRotation
    // (one brush, rotate in place) and the digit reveals (many cell brushes on
    // ONE shared props, each placed at apex − cellOffset, so they stay in
    // phase as one cone).
    private static void BindPlacedRotation(
        Compositor compositor,
        CompositionSurfaceBrush brush,
        CompositionPropertySet props,
        Vector2 neg, Vector2 pos,
        float minSpeedFraction)
    {
        float clampedFraction = Math.Clamp(minSpeedFraction, 0f, 1f);

        // CRITICAL — Composition's expression language is NOT C#. Numeric
        // literals are written without any suffix: `1.0` is a Float, `1` is
        // an Int. A C# `1.0f` would be parsed as `1.0 * f` with `f` a missing
        // variable (default 0), turning `(1.0 - minFrac)` into `-minFrac` and
        // the whole expression into a yo-yo motion. Stay strict on literals.
        var matrixExpr = compositor.CreateExpressionAnimation(
            "Matrix3x2.CreateTranslation(neg) * " +
            "Matrix3x2.CreateRotation(props.Linear * minFrac + props.Eased * (1.0 - minFrac)) * " +
            "Matrix3x2.CreateTranslation(pos)");
        matrixExpr.SetReferenceParameter("props", props);
        matrixExpr.SetVector2Parameter("neg", neg);
        matrixExpr.SetVector2Parameter("pos", pos);
        matrixExpr.SetScalarParameter ("minFrac", clampedFraction);
        brush.StartAnimation("TransformMatrix", matrixExpr);
    }

}

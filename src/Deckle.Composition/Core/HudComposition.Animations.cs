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

    // ApplyVariant animates the PropertySet scalar and this expression
    // mirrors it onto the effect property: a KeyFrameAnimation on the effect
    // brush itself cannot read "this.CurrentValue" reliably while another
    // animation runs on the same property.
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

    // ── Cyclic rotation ─────────────────────────────────────────────────
    //
    // Two scalars on one PropertySet run from startAngle to endAngle over
    // the same period: `Linear` without easing and `Eased` on the
    // cubic-bezier. The brush matrix reads
    //   angle = Linear · f + Eased · (1 − f),   f = minSpeedFraction,
    // so ω(t) = ω_mean · [f + (1 − f) · E'(t)]. Both scalars land on
    // endAngle, so the Forever loop closes with no phase drift.
    //
    // The caller holds the returned PropertySet and must StopAnimation and
    // Dispose it at teardown. Orphaned Forever animations stay on the
    // compositor; enough of them saturate it and Forever animations stop
    // firing.
    //
    // `placement` parks the spinning centre elsewhere:
    // T(−visualCentre)·R(θ)·T(placement). Null rotates in place.
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
        Vector2? placement = null,
        bool animationsEnabled = true)
    {
        var props = CreateRotationPropertySet(
            compositor, periodSeconds, direction, phaseTurns,
            easeP1X, easeP1Y, easeP2X, easeP2Y,
            animationsEnabled);
        BindPlacedRotation(
            compositor, brush, props,
            -visualCentre, placement ?? visualCentre, minSpeedFraction);
        return props;
    }

    // The rotation without a brush, so one rotation can drive several brushes
    // in phase (the six digit reveals). Same ownership rule as StartRotation.
    private static CompositionPropertySet CreateRotationPropertySet(
        Compositor compositor,
        double periodSeconds,
        float direction,
        float phaseTurns,
        float easeP1X, float easeP1Y,
        float easeP2X, float easeP2Y,
        bool animationsEnabled = true)
    {
        float startAngle = MathF.Tau * phaseTurns;

        var props = compositor.CreatePropertySet();
        props.InsertScalar("Linear", startAngle);
        props.InsertScalar("Eased",  startAngle);

        if (animationsEnabled)
        {
            StartRotationPropertySetAnimations(
                compositor, props, periodSeconds, direction, phaseTurns,
                easeP1X, easeP1Y, easeP2X, easeP2Y);
        }

        return props;
    }

    // Starts a continuous rotation from its canonical phase. Callers stop both
    // channels before invoking this helper; re-enabling functional animation
    // therefore resumes from a stable pose instead of replaying an old phase.
    private static void StartRotationPropertySetAnimations(
        Compositor compositor,
        CompositionPropertySet props,
        double periodSeconds,
        float direction,
        float phaseTurns,
        float easeP1X, float easeP1Y,
        float easeP2X, float easeP2Y)
    {
        float startAngle = MathF.Tau * phaseTurns;
        float fullAngle  = MathF.Tau * direction;
        float endAngle   = startAngle + fullAngle;

        props.InsertScalar("Linear", startAngle);
        props.InsertScalar("Eased",  startAngle);

        // A zero-duration Forever KeyFrameAnimation lands on its end state on
        // frame 0 and never advances, which reads as a frozen stroke. The
        // Playground sliders reach 0.
        double clampedPeriod = Math.Max(0.05, periodSeconds);
        var duration = TimeSpan.FromSeconds(clampedPeriod);

        var linearAnim = compositor.CreateScalarKeyFrameAnimation();
        linearAnim.InsertKeyFrame(0f, startAngle);
        linearAnim.InsertKeyFrame(1f, endAngle);
        linearAnim.Duration          = duration;
        linearAnim.IterationBehavior = AnimationIterationBehavior.Forever;
        props.StartAnimation("Linear", linearAnim);

        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(easeP1X, easeP1Y),
            new Vector2(easeP2X, easeP2Y));
        var easedAnim = compositor.CreateScalarKeyFrameAnimation();
        easedAnim.InsertKeyFrame(0f, startAngle);
        easedAnim.InsertKeyFrame(1f, endAngle, easing);
        easedAnim.Duration          = duration;
        easedAnim.IterationBehavior = AnimationIterationBehavior.Forever;
        props.StartAnimation("Eased", easedAnim);

    }

    private static void StopRotationAtCanonicalPhase(
        CompositionPropertySet props, float phaseTurns)
    {
        props.StopAnimation("Linear");
        props.StopAnimation("Eased");
        float angle = MathF.Tau * phaseTurns;
        props.InsertScalar("Linear", angle);
        props.InsertScalar("Eased", angle);
    }

    // T(neg)·R(θ)·T(pos): `neg` = −pivot in brush space, `pos` = where the
    // pivot lands (pos = pivot rotates in place). minSpeedFraction is clamped
    // so a stray value cannot invert or over-amplify the rotation.
    private static void BindPlacedRotation(
        Compositor compositor,
        CompositionSurfaceBrush brush,
        CompositionPropertySet props,
        Vector2 neg, Vector2 pos,
        float minSpeedFraction)
    {
        float clampedFraction = Math.Clamp(minSpeedFraction, 0f, 1f);

        // Composition's expression language is not C#: literals take no
        // suffix (`1.0` is a Float, `1` an Int). A C# `1.0f` is parsed as
        // `1.0 * f` with `f` a missing variable (0), turning `(1.0 - minFrac)`
        // into `-minFrac` and the rotation into a yo-yo.
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

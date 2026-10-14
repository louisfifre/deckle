using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;

namespace Deckle.Composition;

public static partial class HudComposition
{
    private const float CountdownCanonicalSpanTurns = 0.25f; // [0, PI/2]
    private const float CountdownMinimumSpread = 0.0005f;
    private const float CountdownCanonicalRotation = -3f * MathF.PI / 4f;

    // A progress-driven mirrored mask over the Recording outline's material:
    // the same baked cone, posed as the outline poses it (frozen at
    // HuePhaseTurns), on Recording's material (RecordingMaterial: the wheel
    // on Colour, the flat grey on Grey). One mask brush
    // lights two wedges, centred on 12 and 6 o'clock, that widen toward the
    // four corners and fade like the outline's lobes; only its transform reads
    // progress. The gauge is the outline opened by the pause, so the two are
    // tuned together through the Recording* fields.
    public sealed class UtteranceCountdownVisual : IDisposable
    {
        // The gauge's one motion. Every move — following a capture sample,
        // reaching full at a cut, retreating when speech resumes — takes the
        // Windows "fast" duration (167 ms), so the gauge has one feel. The
        // rest curve is the "fast out, slow in" Windows documents for an
        // object quickly returning to rest; following uses no curve, see
        // SetProgress. These two values belong to the design-system holder
        // of named durations and curves once it exists; until then the
        // countdown names them here.
        private static readonly TimeSpan MotionDuration = TimeSpan.FromMilliseconds(167);
        private static readonly Vector2 RestEaseControlPoint1 = new(0f, 0f);
        private static readonly Vector2 RestEaseControlPoint2 = new(0f, 1f);

        private readonly List<IDisposable> _resources = new();
        private readonly List<CompositionSurfaceBrush> _movingBrushes = new();
        private readonly Compositor _compositor;
        private readonly CompositionPropertySet _progress;
        private bool _disposed;

        public ContainerVisual Container { get; }

        internal UtteranceCountdownVisual(
            Compositor compositor,
            Vector2 size,
            CompositionPropertySet progress)
        {
            _compositor = compositor;
            Container = Own(compositor.CreateContainerVisual());
            Container.Size = size;
            _progress = Own(progress);
            using var visibility = compositor.CreateExpressionAnimation("progress.Value > 0 ? 1 : 0");
            visibility.SetReferenceParameter("progress", _progress);
            Container.StartAnimation("Opacity", visibility);
        }

        internal T Own<T>(T resource) where T : IDisposable
        {
            _resources.Add(resource);
            return resource;
        }

        internal void BindProgressMask(
            CompositionSurfaceBrush brush,
            Vector2 centre,
            Vector2 hudSize,
            float startReach,
            float endReach)
        {
            // The expression language follows the same formula as
            // GetUtteranceCountdownTransform, but evaluates it on the
            // Composition render thread. It has no float literal suffixes,
            // so every constant travels as a scalar parameter.
            var progressExpression = Own(_compositor.CreateExpressionAnimation(
                "Matrix3x2.CreateTranslation(-centre) * " +
                "Matrix3x2.CreateRotation(angle) * " +
                "Matrix3x2.CreateScale(Vector2(" +
                "Max(Lerp(startReach, endReach, progress.Value) * aspect, minimumSpread), one)) * " +
                "Matrix3x2.CreateTranslation(centre)"));
            progressExpression.SetReferenceParameter("progress", _progress);
            progressExpression.SetVector2Parameter("centre", centre);
            progressExpression.SetScalarParameter("angle", CountdownCanonicalRotation);
            progressExpression.SetScalarParameter("startReach", startReach);
            progressExpression.SetScalarParameter("endReach", endReach);
            progressExpression.SetScalarParameter("aspect", CountdownAspect(hudSize));
            progressExpression.SetScalarParameter("minimumSpread", CountdownMinimumSpread);
            progressExpression.SetScalarParameter("one", 1f);

            brush.StartAnimation("TransformMatrix", progressExpression);
            _movingBrushes.Add(brush);
        }

        public void SetProgress(float progress, bool interpolate = false)
        {
            if (_disposed) return;

            float value = Math.Clamp(progress, 0f, 1f);
            if (!interpolate)
            {
                _progress.StopAnimation("Value");
                _progress.InsertScalar("Value", value);
                return;
            }

            // One motion for every move, from wherever the gauge stands.
            //
            // A live sample is followed linearly: the next sample, 50 ms
            // later, restarts the move from the point reached, so the gauge
            // glides about one MotionDuration behind the capture with a
            // continuous velocity and a soft onset (each early sample moves
            // only a part of a small gap). An eased curve restarted every
            // sample would lurch — fast then slow, fast then slow. Reaching
            // full at a cut is the same move; the completion hold outlasts it.
            //
            // Speech resuming is one move to rest, so it alone takes the
            // "fast out, slow in" curve. The container opacity expression
            // keeps the visual shown while Value > 0 and hides it when the
            // retreat lands.
            using CompositionEasingFunction easing = value == 0f
                ? _compositor.CreateCubicBezierEasingFunction(
                    RestEaseControlPoint1, RestEaseControlPoint2)
                : _compositor.CreateLinearEasingFunction();
            using var move = _compositor.CreateScalarKeyFrameAnimation();
            move.InsertExpressionKeyFrame(0f, "this.CurrentValue");
            move.InsertKeyFrame(1f, value, easing);
            move.Duration = MotionDuration;
            _progress.StartAnimation("Value", move);
        }

        public void Replay(double seconds, bool animationsEnabled)
        {
            SetProgress(0f);
            if (_disposed) return;

            if (!animationsEnabled)
            {
                SetProgress(1f);
                return;
            }

            using var animation = _compositor.CreateScalarKeyFrameAnimation();
            using var linear = _compositor.CreateLinearEasingFunction();
            animation.InsertKeyFrame(0f, 0f);
            animation.InsertKeyFrame(1f, 1f, linear);
            animation.Duration = TimeSpan.FromSeconds(Math.Clamp(seconds, 0.1, 30));
            _progress.StartAnimation("Value", animation);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Container.StopAnimation("Opacity");
            _progress.StopAnimation("Value");

            foreach (var brush in _movingBrushes)
                brush.StopAnimation("TransformMatrix");

            Container.Children.RemoveAll();
            for (int i = _resources.Count - 1; i >= 0; i--)
                _resources[i].Dispose();
        }
    }

    // Half-width over half-height: turns a reach fraction along the top edge
    // into the tangent of the wedge's half-angle.
    private static float CountdownAspect(Vector2 hudSize)
        => (hudSize.X / 2f) / MathF.Max(1f, hudSize.Y / 2f);

    // The canonical mask is painted with ArcMirror = true and
    // ConicSpanTurns = 0.25, i.e. the slices [0°, 90°] and [180°, 270°] in
    // Win2D's Y-down angle. Rotating by -135° centres them on 12 and 6
    // o'clock. Scaling X by `spread` then sets each wedge's half-angle to
    // atan(spread); with spread = reach · halfWidth / halfHeight the boundary
    // rays cross the top and bottom edges at x = centre ± reach · halfWidth,
    // so the lit edge length grows linearly with progress and meets the four
    // corners exactly at reach 1. Row-vector convention, as System.Numerics.
    public static Matrix3x2 GetUtteranceCountdownTransform(
        Vector2 centre,
        Vector2 hudSize,
        float startReach,
        float endReach,
        float progress)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        float reach = startReach + (endReach - startReach) * p;
        float spread = MathF.Max(reach * CountdownAspect(hudSize), CountdownMinimumSpread);

        return Matrix3x2.CreateTranslation(-centre)
             * Matrix3x2.CreateRotation(CountdownCanonicalRotation)
             * Matrix3x2.CreateScale(spread, 1f)
             * Matrix3x2.CreateTranslation(centre);
    }

    // The mask the gauge's wedges are painted with: the Recording outline's
    // lead and tail fades carried from the outline's span onto the canonical
    // quarter-turn span, with the outline's curve. PaintArcMaskSurface clamps
    // both to the span and, when they overlap, rescales them to meet
    // mid-wedge — the outline's own bell at the defaults. What is kept is the
    // proportion of fade to span, not the angle: progress stretches the
    // wedge, the outline's lobe never moves. A source span of zero paints the
    // outline empty, so the wedges fall back to hard edges rather than divide
    // by it.
    public static ConicArcStrokeConfig GetUtteranceCountdownMaskConfig(
        ConicArcStrokeConfig config)
    {
        var recording = BuildRecordingPaintConfig(config);
        float sourceSpan = recording.ConicSpanTurns;
        float scale = sourceSpan > 0f ? CountdownCanonicalSpanTurns / sourceSpan : 0f;
        return recording with
        {
            ArcMirror = true,
            ConicSpanTurns = CountdownCanonicalSpanTurns,
            ConicLeadFadeTurns = recording.ConicLeadFadeTurns * scale,
            ConicTailFadeTurns = recording.ConicTailFadeTurns * scale,
        };
    }

    public static UtteranceCountdownVisual CreateUtteranceCountdownVisual(
        Compositor compositor,
        Vector2 hostSize,
        Vector2 hudSize,
        ConicArcStrokeConfig config,
        bool isDark,
        UtteranceCountdownConfig countdown,
        float materialOpacity,
        bool animationsEnabled = true)
    {
        float startReach = countdown.StartReach;
        float endReach = countdown.EndReach;

        Vector2 centre = hostSize / 2f;
        var progress = compositor.CreatePropertySet();
        progress.InsertScalar("Value", 0f);

        var visual = new UtteranceCountdownVisual(compositor, hostSize, progress);

        try
        {
            if (startReach == endReach) return visual;
            var device = CanvasDevice.GetSharedDevice();
            EnsureDeviceLostHook(device);
            var graphics = visual.Own(
                CanvasComposition.CreateCompositionGraphicsDevice(compositor, device));

            // The gauge paints the Recording outline's cone, in the outline's
            // pose, so a pause opens the very material the voice was lighting.
            var recording = BuildRecordingPaintConfig(config);

            int square = CoverageSquareSide(hostSize, centre);
            var conic = visual.Own(PaintConicSurface(device, graphics, square, recording));
            var conicBrush = visual.Own(compositor.CreateSurfaceBrush(conic));
            conicBrush.Stretch = CompositionStretch.None;
            // Frozen at the outline's phase: the one-shot pose
            // CreateConicArcStroke bakes under freezeHueRotation. On Colour
            // the pose decides which hues face the edges, so the gauge opens
            // on the very hues the outline shows there; on Grey it shows
            // nothing.
            conicBrush.TransformMatrix =
                Matrix3x2.CreateTranslation(-centre) *
                Matrix3x2.CreateRotation(MathF.Tau * recording.HuePhaseTurns) *
                Matrix3x2.CreateTranslation(centre);

            // Recording's material and hue shift, the values
            // ApplyVariant(Recording) gives the outline.
            var (mix, hueShiftTurns) = recording.MaterialOf(ProcessingVariant.Recording);
            using var gradingFactory = compositor.CreateEffectFactory(
                BuildMaterialMix(
                    new CompositionEffectSourceParameter("Conic"),
                    mix, hueShiftTurns,
                    GreyMaterialColor(isDark ? recording.GreyLightnessDark : recording.GreyLightnessLight)));
            var grading = visual.Own(gradingFactory.CreateBrush());
            grading.SetSourceParameter("Conic", conicBrush);
            CompositionBrush materialSource = grading;

            float offsetX = (hostSize.X - hudSize.X) / 2f;
            float offsetY = (hostSize.Y - hudSize.Y) / 2f;
            // Same silhouette as CreateConicArcStroke: an outline of width t
            // inside the HUD rect shrunk by the inset, clamped like the
            // stroke's. A negative inset pushes the outer half past the HUD
            // rect; whether it shows depends on the host the caller gives,
            // and HudChrono oversizes its host so the contour overflows the
            // card as the outline does.
            float inset = config.InsetDip;
            float silhouetteWidth = hudSize.X - 2f * inset;
            float silhouetteHeight = hudSize.Y - 2f * inset;
            float thickness = Math.Clamp(
                config.StrokeThickness, 0.5f,
                MathF.Max(0.5f, MathF.Min(silhouetteWidth, silhouetteHeight)));

            // The two wedges fade like the outline's lobes — see
            // GetUtteranceCountdownMaskConfig.
            var maskConfig = GetUtteranceCountdownMaskConfig(config);
            // The canonical boundary rays lose sqrt(2) of their vertical
            // reach after the -135 degree rotation. Oversize before scaling
            // so both feathered boundaries still reach the real HUD edges.
            int maskSquare = (int)Math.Ceiling(
                CoverageSquareSide(hostSize, centre) * Math.Sqrt(2));
            var maskSurface = visual.Own(
                PaintArcMaskSurface(
                    device, graphics, maskSquare, maskConfig,
                    Microsoft.UI.Colors.White));

            var contourSurface = visual.Own(graphics.CreateDrawingSurface(
                new Windows.Foundation.Size(hostSize.X, hostSize.Y),
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                DirectXAlphaMode.Premultiplied));
            using (var drawing = CanvasComposition.CreateDrawingSession(contourSurface))
            {
                drawing.Clear(Microsoft.UI.Colors.Transparent);
                drawing.DrawRoundedRectangle(
                    new Windows.Foundation.Rect(
                        offsetX + inset + thickness / 2f,
                        offsetY + inset + thickness / 2f,
                        MathF.Max(0f, silhouetteWidth - thickness),
                        MathF.Max(0f, silhouetteHeight - thickness)),
                    CornerRadiusDip, CornerRadiusDip,
                    Microsoft.UI.Colors.White, thickness);
            }

            var contourBrush = visual.Own(compositor.CreateSurfaceBrush(contourSurface));
            contourBrush.Stretch = CompositionStretch.Fill;

            var maskBrush = visual.Own(compositor.CreateSurfaceBrush(maskSurface));
            maskBrush.Stretch = CompositionStretch.None;
            visual.BindProgressMask(maskBrush, centre, hudSize, startReach, endReach);

            // The cone behind the gauge, for inspection: the live HUD passes
            // 0 and shows only the gauge.
            if (materialOpacity > 0f)
            {
                var materialGraph = new AlphaMaskEffect
                {
                    Source = new CompositionEffectSourceParameter("Material"),
                    AlphaMask = new CompositionEffectSourceParameter("Arc"),
                };
                using var materialFactory = compositor.CreateEffectFactory(materialGraph);
                var materialBrush = visual.Own(materialFactory.CreateBrush());
                materialBrush.SetSourceParameter("Material", materialSource);
                materialBrush.SetSourceParameter("Arc", maskBrush);

                var materialSprite = visual.Own(compositor.CreateSpriteVisual());
                materialSprite.Size = hostSize;
                materialSprite.Brush = materialBrush;
                materialSprite.Opacity = Math.Clamp(materialOpacity, 0f, 1f);
                visual.Container.Children.InsertAtTop(materialSprite);
            }

            var contourGraph = new AlphaMaskEffect
            {
                Source = new AlphaMaskEffect
                {
                    Source = new CompositionEffectSourceParameter("Material"),
                    AlphaMask = new CompositionEffectSourceParameter("Arc"),
                },
                AlphaMask = new CompositionEffectSourceParameter("Contour"),
            };
            using var contourFactory = compositor.CreateEffectFactory(contourGraph);
            var contourEffectBrush = visual.Own(contourFactory.CreateBrush());
            contourEffectBrush.SetSourceParameter("Material", materialSource);
            contourEffectBrush.SetSourceParameter("Arc", maskBrush);
            contourEffectBrush.SetSourceParameter("Contour", contourBrush);

            var contourSprite = visual.Own(compositor.CreateSpriteVisual());
            contourSprite.Size = hostSize;
            contourSprite.Brush = contourEffectBrush;
            visual.Container.Children.InsertAtTop(contourSprite);

            return visual;
        }
        catch
        {
            visual.Dispose();
            throw;
        }
    }


}

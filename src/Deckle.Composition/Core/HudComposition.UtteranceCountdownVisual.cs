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

    // A progress-driven mirrored mask over the same animated, graded conic
    // material used by the processing stroke. One mask brush lights two wedges,
    // centred on 12 and 6 o'clock, that widen toward the four corners; only its
    // transform reads progress.
    public sealed class UtteranceCountdownVisual : IDisposable
    {
        // Windows motion guidance: the fast duration (167 ms) and the
        // "fast out, slow in" curve, documented for an object quickly
        // returning to rest.
        private static readonly TimeSpan RetreatDuration = TimeSpan.FromMilliseconds(167);
        private static readonly Vector2 RetreatEaseControlPoint1 = new(0f, 0f);
        private static readonly Vector2 RetreatEaseControlPoint2 = new(0f, 1f);

        private readonly List<IDisposable> _resources = new();
        private readonly List<CompositionSurfaceBrush> _movingBrushes = new();
        private readonly Compositor _compositor;
        private readonly CompositionPropertySet _progress;
        private CompositionPropertySet? _rotationProps;
        private bool _disposed;

        public ContainerVisual Container { get; }

        internal UtteranceCountdownVisual(
            Compositor compositor,
            Vector2 size,
            CompositionPropertySet progress,
            CompositionPropertySet? rotationProps)
        {
            _compositor = compositor;
            Container = Own(compositor.CreateContainerVisual());
            Container.Size = size;
            _progress = Own(progress);
            using var visibility = compositor.CreateExpressionAnimation("progress.Value > 0 ? 1 : 0");
            visibility.SetReferenceParameter("progress", _progress);
            Container.StartAnimation("Opacity", visibility);
            _rotationProps = rotationProps;
            if (_rotationProps is not null)
                Own(_rotationProps);
        }

        internal T Own<T>(T resource) where T : IDisposable
        {
            _resources.Add(resource);
            return resource;
        }

        internal void SetRotationProps(CompositionPropertySet rotationProps, CompositionSurfaceBrush brush)
        {
            _rotationProps = Own(rotationProps);
            _movingBrushes.Add(brush);
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
            if (!interpolate || value == 1f)
            {
                _progress.StopAnimation("Value");
                _progress.InsertScalar("Value", value);
                return;
            }

            if (value == 0f)
            {
                // Speech resumed: the wedges ease back to the edge centres.
                // The container opacity expression keeps the visual shown
                // while Value > 0 and hides it when the retreat lands.
                using var retreat = _compositor.CreateScalarKeyFrameAnimation();
                using var easing = _compositor.CreateCubicBezierEasingFunction(
                    RetreatEaseControlPoint1, RetreatEaseControlPoint2);
                retreat.InsertExpressionKeyFrame(0f, "this.CurrentValue");
                retreat.InsertKeyFrame(1f, 0f, easing);
                retreat.Duration = RetreatDuration;
                _progress.StartAnimation("Value", retreat);
                return;
            }

            using var animation = _compositor.CreateScalarKeyFrameAnimation();
            using var linear = _compositor.CreateLinearEasingFunction();
            animation.InsertExpressionKeyFrame(0f, "this.CurrentValue");
            animation.InsertKeyFrame(1f, value, linear);
            animation.Duration = TimeSpan.FromMilliseconds(50);
            _progress.StartAnimation("Value", animation);
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
            if (_rotationProps is not null)
            {
                _rotationProps.StopAnimation("Linear");
                _rotationProps.StopAnimation("Eased");
            }

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

    public static UtteranceCountdownVisual CreateUtteranceCountdownVisual(
        Compositor compositor,
        Vector2 hostSize,
        Vector2 hudSize,
        ConicArcStrokeConfig config,
        bool isDark,
        UtteranceCountdownConfig countdown,
        float materialOpacity,
        bool maskOnly = false,
        bool animationsEnabled = true)
    {
        float startReach = countdown.StartReach;
        float endReach = countdown.EndReach;

        Vector2 centre = hostSize / 2f;
        var progress = compositor.CreatePropertySet();
        progress.InsertScalar("Value", 0f);

        CompositionPropertySet? rotationProps = null;
        var visual = new UtteranceCountdownVisual(
            compositor, hostSize, progress, rotationProps);

        try
        {
            if (startReach == endReach) return visual;
            var device = CanvasDevice.GetSharedDevice();
            EnsureDeviceLostHook(device);
            var graphics = visual.Own(
                CanvasComposition.CreateCompositionGraphicsDevice(compositor, device));

            CompositionBrush materialSource;
            if (maskOnly)
            {
                materialSource = visual.Own(compositor.CreateColorBrush(
                    isDark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black));
            }
            else
            {
                int square = CoverageSquareSide(hostSize, centre);
                var conic = visual.Own(PaintConicSurface(device, graphics, square, config));
                var conicBrush = visual.Own(compositor.CreateSurfaceBrush(conic));
                conicBrush.Stretch = CompositionStretch.None;
                rotationProps = StartRotation(
                    compositor, conicBrush, centre,
                    config.HuePeriodSeconds, config.HueDirection,
                    config.HuePhaseTurns, config.HueEaseP1X, config.HueEaseP1Y,
                    config.HueEaseP2X, config.HueEaseP2Y,
                    config.HueMinSpeedFraction,
                    animationsEnabled: animationsEnabled);
                visual.SetRotationProps(rotationProps, conicBrush);

                using var gradingFactory = compositor.CreateEffectFactory(
                    BuildVariantGrading(
                        new CompositionEffectSourceParameter("Conic"),
                        config, ProcessingVariant.Transcribing, isDark));
                var grading = visual.Own(gradingFactory.CreateBrush());
                grading.SetSourceParameter("Conic", conicBrush);
                materialSource = grading;
            }

            float offsetX = (hostSize.X - hudSize.X) / 2f;
            float offsetY = (hostSize.Y - hudSize.Y) / 2f;
            // Same silhouette as CreateConicArcStroke: an outline of width t
            // inside the HUD rect shrunk by the inset, clamped like the
            // stroke's. A negative inset pushes the outer half past the
            // surface, exactly as the live stroke's outer half falls
            // outside the HWND.
            float inset = config.InsetDip;
            float silhouetteWidth = hudSize.X - 2f * inset;
            float silhouetteHeight = hudSize.Y - 2f * inset;
            float thickness = Math.Clamp(
                config.StrokeThickness, 0.5f,
                MathF.Max(0.5f, MathF.Min(silhouetteWidth, silhouetteHeight)));

            // The fade is authored in turns of the full opening (each wedge at
            // EndReach) and rescaled into the canonical quarter-turn span.
            float fullOpeningTurns = MathF.Max(
                2f * MathF.Atan(endReach * CountdownAspect(hudSize)) / MathF.Tau, 0.0001f);
            float edgeFade = Math.Clamp(
                countdown.EdgeFadeTurns / fullOpeningTurns * CountdownCanonicalSpanTurns,
                0f, CountdownCanonicalSpanTurns);
            var maskConfig = config with
            {
                ArcMirror = true,
                ConicSpanTurns = CountdownCanonicalSpanTurns,
                ConicLeadFadeTurns = edgeFade,
                ConicTailFadeTurns = edgeFade,
                ConicFadeCurve = MathF.Max(0.01f, countdown.FadeCurve),
            };
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

            bool showMaterial = maskOnly || materialOpacity > 0f;
            if (showMaterial)
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
                materialSprite.Opacity = maskOnly
                    ? 1f : Math.Clamp(materialOpacity, 0f, 1f);
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

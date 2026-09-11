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

    // Progress-driven mirrored masks over the same animated, graded conic material
    // used by the processing stroke. The conic is one shared brush; only the two
    // mask transforms read progress.
    public sealed class UtteranceCountdownVisual : IDisposable
    {
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
            float startRadians,
            float endRadians,
            bool mirror)
        {
            // The expression language follows the same formulas as
            // GetUtteranceCountdownTransform, but evaluates them on the
            // Composition render thread.
            const string front = "ATan(Lerp(startSlope, endSlope, progress.Value))";
            string signedSpread = $"Tan((startRadians - {front}) * 0.5)";
            string spread = $"({signedSpread} < 0 ? -Max(-{signedSpread}, minimumSpread) : Max({signedSpread}, minimumSpread))";
            var progressExpression = Own(_compositor.CreateExpressionAnimation(
                "Matrix3x2.CreateTranslation(-centre) * " +
                "Matrix3x2.CreateRotation(-quarter) * " +
                $"Matrix3x2.CreateScale(Vector2(one, {spread})) * " +
                $"Matrix3x2.CreateRotation((startRadians + {front}) * 0.5) * " +
                "Matrix3x2.CreateScale(reflection) * " +
                "Matrix3x2.CreateTranslation(centre)"));
            progressExpression.SetReferenceParameter("progress", _progress);
            progressExpression.SetVector2Parameter("centre", centre);
            progressExpression.SetScalarParameter("startRadians", startRadians);
            progressExpression.SetScalarParameter("startSlope", MathF.Tan(startRadians));
            progressExpression.SetScalarParameter("endSlope", MathF.Tan(endRadians));
            progressExpression.SetScalarParameter("quarter", MathF.PI / 4f);
            progressExpression.SetScalarParameter("one", 1f);
            progressExpression.SetScalarParameter("minimumSpread", CountdownMinimumSpread);
            progressExpression.SetVector2Parameter(
                "reflection", new Vector2(mirror ? -1f : 1f, 1f));

            brush.StartAnimation("TransformMatrix", progressExpression);
            _movingBrushes.Add(brush);
        }

        public void SetProgress(float progress, bool interpolate = false)
        {
            if (_disposed) return;

            float value = Math.Clamp(progress, 0f, 1f);
            if (!interpolate || value == 0f || value == 1f)
            {
                _progress.StopAnimation("Value");
                _progress.InsertScalar("Value", value);
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

    public static float UtteranceCountdownAngleLimit(Vector2 hudSize)
    {
        float halfWidth = MathF.Max(1f, hudSize.X / 2f);
        float straightHalfHeight = MathF.Max(0f, hudSize.Y / 2f - CornerRadiusDip);
        return MathF.Atan(straightHalfHeight / halfWidth) * 180f / MathF.PI;
    }

    public static Matrix3x2 GetUtteranceCountdownTransform(
        Vector2 centre,
        float startDegrees,
        float endDegrees,
        float progress,
        bool mirror)
    {
        float p = Math.Clamp(progress, 0f, 1f);
        float start = Math.Clamp(startDegrees, -89f, 89f) * MathF.PI / 180f;
        float end = Math.Clamp(endDegrees, -89f, 89f) * MathF.PI / 180f;
        float startSlope = MathF.Tan(start);
        float endSlope = MathF.Tan(end);
        float front = MathF.Atan(startSlope + (endSlope - startSlope) * p);
        float spread = MathF.Tan((start - front) * 0.5f);
        if (MathF.Abs(spread) < CountdownMinimumSpread)
            spread = spread < 0f ? -CountdownMinimumSpread : CountdownMinimumSpread;
        float midpoint = (start + front) * 0.5f;

        return Matrix3x2.CreateTranslation(-centre)
             * Matrix3x2.CreateRotation(-MathF.PI / 4f)
             * Matrix3x2.CreateScale(1f, spread)
             * Matrix3x2.CreateRotation(midpoint)
             * Matrix3x2.CreateScale(mirror ? -1f : 1f, 1f)
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
        float angleLimit = UtteranceCountdownAngleLimit(hudSize);
        float startDegrees = Math.Clamp(countdown.StartDegrees, -angleLimit, angleLimit);
        float endDegrees = Math.Clamp(countdown.EndDegrees, -angleLimit, angleLimit);

        Vector2 centre = hostSize / 2f;
        var progress = compositor.CreatePropertySet();
        progress.InsertScalar("Value", 0f);

        CompositionPropertySet? rotationProps = null;
        var visual = new UtteranceCountdownVisual(
            compositor, hostSize, progress, rotationProps);

        try
        {
            if (startDegrees == endDegrees) return visual;
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
            float thickness = Math.Clamp(
                countdown.StrokeThickness, 0.5f, MathF.Max(0.5f, hudSize.X / 2f));

            float fullOpening = MathF.Max(MathF.Abs(startDegrees - endDegrees) / 360f, 0.0001f);
            var maskConfig = config with
            {
                ArcMirror = false,
                ConicSpanTurns = CountdownCanonicalSpanTurns,
                ConicLeadFadeTurns = Math.Clamp(
                    countdown.LeadFadeTurns / fullOpening * CountdownCanonicalSpanTurns,
                    0f, CountdownCanonicalSpanTurns),
                ConicTailFadeTurns = Math.Clamp(
                    countdown.TailFadeTurns / fullOpening * CountdownCanonicalSpanTurns,
                    0f, CountdownCanonicalSpanTurns),
                ConicFadeCurve = MathF.Max(0.01f, countdown.FadeCurve),
            };
            // The canonical boundary rays lose sqrt(2) of their horizontal
            // reach after the -45 degree rotation. Oversize before compressing
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
                drawing.FillRectangle(
                    new Windows.Foundation.Rect(offsetX, offsetY, thickness, hudSize.Y),
                    Microsoft.UI.Colors.White);
                drawing.FillRectangle(
                    new Windows.Foundation.Rect(
                        offsetX + hudSize.X - thickness, offsetY,
                        thickness, hudSize.Y),
                    Microsoft.UI.Colors.White);
            }

            var contourBrush = visual.Own(compositor.CreateSurfaceBrush(contourSurface));
            contourBrush.Stretch = CompositionStretch.Fill;

            bool showMaterial = maskOnly || materialOpacity > 0f;
            for (int side = 0; side < 2; side++)
            {
                var maskBrush = visual.Own(compositor.CreateSurfaceBrush(maskSurface));
                maskBrush.Stretch = CompositionStretch.None;
                visual.BindProgressMask(
                    maskBrush, centre, startDegrees * MathF.PI / 180f,
                    endDegrees * MathF.PI / 180f, mirror: side == 1);

                float leftInset = offsetX + hudSize.X - thickness;
                float rightInset = hostSize.X - offsetX - thickness;

                if (showMaterial)
                {
                    var materialGraph = new AlphaMaskEffect
                    {
                        Source = new CompositionEffectSourceParameter("Material"),
                        AlphaMask = new CompositionEffectSourceParameter("Arc"),
                    };
                    using var materialFactory =
                        compositor.CreateEffectFactory(materialGraph);
                    var materialBrush = visual.Own(materialFactory.CreateBrush());
                    materialBrush.SetSourceParameter("Material", materialSource);
                    materialBrush.SetSourceParameter("Arc", maskBrush);

                    var materialSprite = visual.Own(compositor.CreateSpriteVisual());
                    materialSprite.Size = hostSize;
                    materialSprite.Brush = materialBrush;
                    materialSprite.Opacity = maskOnly
                        ? 1f : Math.Clamp(materialOpacity, 0f, 1f);
                    var materialClip = visual.Own(compositor.CreateInsetClip());
                    if (side == 0) materialClip.LeftInset = hostSize.X / 2f;
                    else materialClip.RightInset = hostSize.X / 2f;
                    materialSprite.Clip = materialClip;
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
                using var contourFactory =
                    compositor.CreateEffectFactory(contourGraph);
                var contourEffectBrush = visual.Own(contourFactory.CreateBrush());
                contourEffectBrush.SetSourceParameter("Material", materialSource);
                contourEffectBrush.SetSourceParameter("Arc", maskBrush);
                contourEffectBrush.SetSourceParameter("Contour", contourBrush);

                var contourSprite = visual.Own(compositor.CreateSpriteVisual());
                contourSprite.Size = hostSize;
                contourSprite.Brush = contourEffectBrush;
                var contourClip = visual.Own(compositor.CreateInsetClip());
                contourClip.TopInset = offsetY;
                contourClip.BottomInset = offsetY;
                if (side == 0) contourClip.LeftInset = leftInset;
                else contourClip.RightInset = rightInset;
                contourSprite.Clip = contourClip;
                visual.Container.Children.InsertAtTop(contourSprite);
            }

            return visual;
        }
        catch
        {
            visual.Dispose();
            throw;
        }
    }


}

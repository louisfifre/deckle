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
    // Counters for the "animation freezes after N rebuilds" failure: a missing
    // Dispose makes the live count climb while the compositor saturates and
    // Forever animations go silent.
    private static int _creationCounter;
    private static int _liveStrokeCount;
    internal static int TotalStrokesCreated => _creationCounter;
    public   static int LiveStrokeCount     => _liveStrokeCount;
    internal static event Action<int, string>? StrokeLifecycle; // (creationId, event)

    // ── DeviceLost hook ──────────────────────────────────────────────────
    // Every surface here is baked on Win2D's process-wide shared device. When
    // the GPU goes away (TDR, driver reset, contention with whisper.cpp's
    // Vulkan backend on the same GPU) the surfaces go invalid and the brushes
    // sample black, which reads as a frozen rotation while the animations
    // still tick. Nothing recovers here: the event only reports the loss.
    //
    // Raised on Win2D's thread: a subscriber that touches UI marshals itself.
    internal static event Action<string>? CanvasDeviceLost;
    private static bool _deviceLostHooked;
    private static readonly object _deviceLostLock = new();

    // Hooks once per process; the lock keeps two concurrent first calls from
    // subscribing twice.
    private static void EnsureDeviceLostHook(CanvasDevice device)
    {
        if (_deviceLostHooked) return;
        lock (_deviceLostLock)
        {
            if (_deviceLostHooked) return;
            device.DeviceLost += OnCanvasDeviceLost;
            _deviceLostHooked = true;
        }
    }

    private static void OnCanvasDeviceLost(CanvasDevice sender, object args)
    {
        // Win2D's DeviceLost args are empty, so the reason is synthesised.
        CanvasDeviceLost?.Invoke($"CanvasDevice.DeviceLost fired (live strokes = {_liveStrokeCount})");
    }

    public sealed class ProcessingStroke : IDisposable
    {
        public ContainerVisual Visual { get; }
        public int CreationId { get; }

        // The live Mix / HueAngle / Grey entries ApplyVariant animates. The
        // digit reveals bind their own material mix to them: a built effect
        // brush cannot be the source of another effect, so the share happens
        // at the PropertySet.
        public CompositionPropertySet EffectProps => _effectProps;
        public ConicArcStrokeConfig Config => _config;

        private readonly Compositor _compositor;
        private readonly CompositionPropertySet _effectProps;
        private readonly SpriteVisual _strokeVisual;
        private readonly ConicArcStrokeConfig _config;

        // Held so Dispose can stop and release them: disposing only the
        // container leaves the brushes, surfaces and Forever animations on
        // the compositor, where they accumulate across rebuilds.
        private readonly CompositionSurfaceBrush _conicBrush;
        private readonly CompositionSurfaceBrush _arcMaskBrush;
        private readonly CompositionSurfaceBrush _strokeMaskBrush;
        private readonly CompositionEffectBrush  _effectBrush;
        private readonly CompositionDrawingSurface _conicSurface;
        private readonly CompositionDrawingSurface _arcMaskSurface;
        private readonly CompositionDrawingSurface _strokeMaskSurface;
        // Null when the rotation is frozen on a static matrix.
        private readonly CompositionPropertySet? _hueRotationProps;
        private readonly CompositionPropertySet? _arcRotationProps;
        private volatile bool _animationsEnabled;

        private bool _disposed;

        internal ProcessingStroke(
            ContainerVisual visual,
            Compositor compositor,
            CompositionPropertySet effectProps,
            SpriteVisual strokeVisual,
            ConicArcStrokeConfig config,
            CompositionSurfaceBrush conicBrush,
            CompositionSurfaceBrush arcMaskBrush,
            CompositionSurfaceBrush strokeMaskBrush,
            CompositionEffectBrush  effectBrush,
            CompositionDrawingSurface conicSurface,
            CompositionDrawingSurface arcMaskSurface,
            CompositionDrawingSurface strokeMaskSurface,
            CompositionPropertySet? hueRotationProps,
            CompositionPropertySet? arcRotationProps,
            bool animationsEnabled)
        {
            Visual       = visual;
            _compositor  = compositor;
            _effectProps = effectProps;
            _strokeVisual = strokeVisual;
            _config      = config;
            _conicBrush         = conicBrush;
            _arcMaskBrush       = arcMaskBrush;
            _strokeMaskBrush    = strokeMaskBrush;
            _effectBrush        = effectBrush;
            _conicSurface       = conicSurface;
            _arcMaskSurface     = arcMaskSurface;
            _strokeMaskSurface  = strokeMaskSurface;
            _hueRotationProps   = hueRotationProps;
            _arcRotationProps   = arcRotationProps;
            _animationsEnabled  = animationsEnabled;

            CreationId = System.Threading.Interlocked.Increment(ref _creationCounter);
            System.Threading.Interlocked.Increment(ref _liveStrokeCount);
            StrokeLifecycle?.Invoke(CreationId, "created");
        }

        // Safe to call repeatedly (state entries, live theme changes): each
        // call overrides any blend in flight and starts from the current
        // value ("this.CurrentValue").
        public void ApplyVariant(ProcessingVariant variant, bool isDark)
        {
            // The grey follows the theme on every variant, colour included:
            // a Rewriting → Transcribing blend then lands on the right grey
            // instead of passing through the other theme's.
            var grey = GreyMaterialColor(
                isDark ? _config.GreyLightnessDark : _config.GreyLightnessLight);
            var (mix, hueShiftTurns) = _config.MaterialOf(variant);
            var (opacity, blendSeconds) = variant switch
            {
                // Opacity unused: UpdateLevel owns it, skipped below.
                ProcessingVariant.Recording    => (0f, _config.RecordingBlendSeconds),
                ProcessingVariant.Transcribing => (_config.TranscribingOpacity, _config.TranscribingBlendSeconds),
                ProcessingVariant.Rewriting    => (_config.RewritingOpacity, _config.RewritingBlendSeconds),
                _ => throw new ArgumentOutOfRangeException(nameof(variant)),
            };

            var duration = TimeSpan.FromSeconds(Math.Max(0.001, blendSeconds));
            float hueAngleRadians = hueShiftTurns * MathF.Tau;

            if (!_animationsEnabled)
            {
                SetScalar(_effectProps, "Mix", mix);
                SetScalar(_effectProps, "HueAngle", hueAngleRadians);
                _effectProps.StopAnimation("Grey");
                _effectProps.InsertColor("Grey", grey);
                if (variant != ProcessingVariant.Recording)
                    SetScalar(_strokeVisual, "Opacity", opacity);
                return;
            }

            AnimateScalar(_effectProps, "Mix",      mix,             duration);
            AnimateScalar(_effectProps, "HueAngle", hueAngleRadians, duration);
            AnimateColor (_effectProps, "Grey",     grey,            duration);

            // Recording's Opacity belongs to UpdateLevel: a theme change
            // mid-recording must not knock the outline back to silence.
            if (variant != ProcessingVariant.Recording)
                AnimateScalar(_strokeVisual, "Opacity", opacity, duration);
        }

        // Called on the recording audio thread (HudChrono.UpdateAudioLevel):
        // CompositionPropertySet and StartAnimation are thread-safe per
        // Composition's contract, so there is no DispatcherQueue hop. Each
        // key-frame starts from "this.CurrentValue", so overlapping calls blend
        // from wherever the previous one reached and the render thread
        // interpolates between RMS samples at the display rate.
        // Recording strokes only: elsewhere it would fight ApplyVariant's
        // opacity, so HudChrono gates the call.
        public void UpdateLevel(float level)
        {
            if (!_animationsEnabled) return;

            float clamped = Math.Clamp(level, 0f, 1f);
            var anim = _compositor.CreateScalarKeyFrameAnimation();
            anim.InsertExpressionKeyFrame(0f, "this.CurrentValue");
            anim.InsertKeyFrame(1f, clamped);
            anim.Duration = TimeSpan.FromMilliseconds(50);
            _strokeVisual.StartAnimation("Opacity", anim);
        }

        // Applies the functional HUD animation preference live. Disabling
        // cancels finite blends, parks continuous rotations at their canonical
        // phase and gives Recording a steady neutral outline. Enabling restarts
        // only the continuous rotations; no past blend or mic sample is replayed.
        public void SetAnimationsEnabled(
            bool enabled,
            ProcessingVariant variant,
            bool isDark)
        {
            if (_animationsEnabled == enabled) return;
            _animationsEnabled = enabled;

            if (_hueRotationProps is not null)
            {
                StopRotationAtCanonicalPhase(_hueRotationProps, _config.HuePhaseTurns);
                if (enabled)
                {
                    StartRotationPropertySetAnimations(
                        _compositor, _hueRotationProps,
                        _config.HuePeriodSeconds, _config.HueDirection, _config.HuePhaseTurns,
                        _config.HueEaseP1X, _config.HueEaseP1Y,
                        _config.HueEaseP2X, _config.HueEaseP2Y);
                }
            }

            if (_arcRotationProps is not null)
            {
                StopRotationAtCanonicalPhase(_arcRotationProps, _config.ArcPhaseTurns);
                if (enabled)
                {
                    StartRotationPropertySetAnimations(
                        _compositor, _arcRotationProps,
                        _config.ArcPeriodSeconds, _config.ArcDirection, _config.ArcPhaseTurns,
                        _config.ArcEaseP1X, _config.ArcEaseP1Y,
                        _config.ArcEaseP2X, _config.ArcEaseP2Y);
                }
            }

            if (!enabled)
            {
                ApplyVariant(variant, isDark);
                if (variant == ProcessingVariant.Recording)
                    SetScalar(_strokeVisual, "Opacity", 1f);
            }
        }

        private void AnimateScalar(
            CompositionObject target, string property, float value, TimeSpan duration)
        {
            var anim = _compositor.CreateScalarKeyFrameAnimation();
            anim.InsertExpressionKeyFrame(0f, "this.CurrentValue");
            anim.InsertKeyFrame(1f, value);
            anim.Duration = duration;
            target.StartAnimation(property, anim);
        }

        // Interpolated in RGB: between two greys every space gives greys,
        // and RGB is the one Composition evaluates without a conversion.
        private void AnimateColor(
            CompositionPropertySet target, string property, Color value, TimeSpan duration)
        {
            var anim = _compositor.CreateColorKeyFrameAnimation();
            anim.InterpolationColorSpace = CompositionColorSpace.Rgb;
            anim.InsertExpressionKeyFrame(0f, "this.CurrentValue");
            anim.InsertKeyFrame(1f, value);
            anim.Duration = duration;
            target.StartAnimation(property, anim);
        }

        private static void SetScalar(
            CompositionObject target, string property, float value)
        {
            target.StopAnimation(property);
            switch (target)
            {
                case CompositionPropertySet propertySet:
                    propertySet.InsertScalar(property, value);
                    break;
                case SpriteVisual visual when property == "Opacity":
                    visual.Opacity = value;
                    break;
                default:
                    throw new ArgumentException("Unsupported scalar target.", nameof(target));
            }
        }

        // Two phases so no animation fires on a freed resource: stop every
        // animation at its source, then dispose the native resources. A missed
        // Dispose leaves the native handle alive, the freeze-after-N-rebuilds
        // symptom.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // ── 1. Stop all animations ───────────────────────────────
            // The three effect-brush expressions reference _effectProps. Sever
            // them first: otherwise a render tick between the disposals
            // evaluates an expression against a freed PropertySet and crashes.
            UnbindMaterialMix(_effectBrush);

            try { _conicBrush.StopAnimation("TransformMatrix");   } catch { }
            try { _arcMaskBrush.StopAnimation("TransformMatrix"); } catch { }
            try { _strokeVisual.StopAnimation("Opacity");         } catch { }

            try { _effectProps.StopAnimation("Mix");      } catch { }
            try { _effectProps.StopAnimation("HueAngle"); } catch { }
            try { _effectProps.StopAnimation("Grey");     } catch { }

            if (_hueRotationProps is not null)
            {
                try { _hueRotationProps.StopAnimation("Linear"); } catch { }
                try { _hueRotationProps.StopAnimation("Eased");  } catch { }
            }
            if (_arcRotationProps is not null)
            {
                try { _arcRotationProps.StopAnimation("Linear"); } catch { }
                try { _arcRotationProps.StopAnimation("Eased");  } catch { }
            }

            // ── 2. Dispose resources ─────────────────────────────────
            // Effect brush before _effectProps: the brush holds the expressions
            // that read it.
            try { _effectBrush.Dispose();     } catch { }
            try { _conicBrush.Dispose();      } catch { }
            try { _arcMaskBrush.Dispose();    } catch { }
            try { _strokeMaskBrush.Dispose(); } catch { }

            try { _conicSurface.Dispose();      } catch { }
            try { _arcMaskSurface.Dispose();    } catch { }
            try { _strokeMaskSurface.Dispose(); } catch { }

            try { _hueRotationProps?.Dispose(); } catch { }
            try { _arcRotationProps?.Dispose(); } catch { }
            try { _effectProps.Dispose();       } catch { }

            try { _strokeVisual.Dispose(); } catch { }
            try { Visual.Dispose();        } catch { }

            System.Threading.Interlocked.Decrement(ref _liveStrokeCount);
            StrokeLifecycle?.Invoke(CreationId, "disposed");
        }
    }
}

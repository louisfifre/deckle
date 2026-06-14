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
    // Live handle to a processing stroke created by CreateProcessingStroke.
    //
    // Wraps the ContainerVisual (attach point for XAML) and the animable
    // CompositionPropertySet that drives the SaturationEffect /
    // HueRotationEffect / ExposureEffect properties on the live pipeline.
    // ApplyVariant blends the PropertySet scalars + SpriteVisual.Opacity
    // from their current values to the variant targets over BlendSeconds
    // — no surface rebuild, no lag.
    // Tracks stroke creations / disposals for debugging the "animation
    // freezes after N rebuilds" class of bugs. Each stroke gets a unique
    // CreationId; when _liveStrokeCount grows unbounded (dispose missing)
    // the compositor saturates and Forever animations go silent.
    // Subscribers (like HudPlayground) read these via StrokeLifecycle
    // events below.
    private static int _creationCounter;
    private static int _liveStrokeCount;
    internal static int TotalStrokesCreated => _creationCounter;
    public   static int LiveStrokeCount     => _liveStrokeCount;
    internal static event Action<int, string>? StrokeLifecycle; // (creationId, event)

    // ── DeviceLost hook ──────────────────────────────────────────────────
    // Win2D's CanvasDevice.GetSharedDevice() returns a process-wide D3D11
    // device. If the GPU goes away (driver reset, TDR, Vulkan/D3D contention
    // with the whisper.cpp Vulkan backend running on the same GPU),
    // every CompositionDrawingSurface we baked onto that device becomes
    // invalid — conic surface, arc mask surface, stroke silhouette surface.
    // The compositor keeps rendering but the brushes sample black, which
    // reads as "the rotation froze" even though the expression animation is
    // still ticking underneath. We can't cure the device loss from here
    // (recovery = recreate CanvasDevice + repaint surfaces in the right
    // thread), but we can *observe* it — if DeviceLost fires right when
    // a freeze is observed, the Composition leak (fixed via Dispose) is
    // not the whole story and we need a device-recovery path.
    //
    // The handler runs on whatever thread Win2D raises the event on —
    // subscribers that touch UI must marshal themselves.
    internal static event Action<string>? CanvasDeviceLost;
    private static bool _deviceLostHooked;
    private static readonly object _deviceLostLock = new();

    // Called lazily by CreateConicArcStroke the first time it grabs the
    // shared CanvasDevice, so we attach exactly once per process lifetime.
    // Lock + flag guard against the rare case where two strokes are
    // created concurrently on the UI thread (should not happen, but the
    // cost of double-hooking would be two event fires per loss — cheap
    // to prevent).
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
        // Re-raise for any subscriber (HudPlayground instrumentation,
        // future device-recovery code). String arg is a human-readable
        // reason — Win2D's event args are empty, so we synthesise one.
        CanvasDeviceLost?.Invoke($"CanvasDevice.DeviceLost fired (live strokes = {_liveStrokeCount})");
    }

    public sealed class ProcessingStroke : IDisposable
    {
        public ContainerVisual Visual { get; }
        public int CreationId { get; }

        // ── Shared conic material (read-only accessors) ──────────────────────
        // Exposed so the chrono digits can become "windows on the same living
        // material" as the stroke (F1 — one conic behind the whole HUD): a
        // digit overlay builds its own AlphaMaskEffect(Source = Sat/Hue/Exp(this
        // conic), Mask = glyph) reusing the SAME surface, rotation PropertySet
        // and grading PropertySet the stroke samples — so the texture, its
        // breathing rotation and its colour grading stay in lock-step across
        // digits and contour. The brush-combining table forbids feeding the
        // stroke's *built* effect brush into another effect (CompositionEffect
        // → EffectBrush.SetSourceParameter = NO), so the share happens one level
        // down, at the surface + the two PropertySets — each consumer rebuilds
        // its own graph from them.
        //
        // ConicSurface — the static per-pixel OKLCh palette (the "material").
        public CompositionDrawingSurface ConicSurface => _conicSurface;
        // HueRotationProps — the Linear/Eased scalars whose blend StartRotation
        // turns into the conic brush's TransformMatrix. Null only when the hue
        // rotation is frozen (Recording with RecordingHuePeriodSeconds = 0);
        // Transcribing / Rewriting (the only states that swipe) always spin, so
        // it is non-null whenever a digit reveal runs.
        public CompositionPropertySet? HueRotationProps => _hueRotationProps;
        // EffectProps — the live Saturation / HueAngle / Exposure scalars that
        // ApplyVariant animates. A digit graph binds its own Sat/Hue/Exp slots
        // to these so the grading blend is shared, not duplicated.
        public CompositionPropertySet EffectProps => _effectProps;
        // Config — paint-time + animation knobs (the digit needs HuePeriod /
        // HueDirection / HueMinSpeedFraction / HuePhaseTurns to rebuild a
        // rotation expression that matches the stroke's exactly).
        public ConicArcStrokeConfig Config => _config;

        private readonly Compositor _compositor;
        private readonly CompositionPropertySet _effectProps;
        private readonly SpriteVisual _strokeVisual;
        private readonly ConicArcStrokeConfig _config;

        // Composition graph refs — kept so Dispose() can stop animations
        // and release native handles explicitly. Without these, disposing
        // only the container leaves the brushes, surfaces, propertysets
        // and their Forever animations live on the compositor — they
        // accumulate across rebuilds until the compositor saturates.
        private readonly CompositionSurfaceBrush _conicBrush;
        private readonly CompositionSurfaceBrush _arcMaskBrush;
        private readonly CompositionSurfaceBrush _strokeMaskBrush;
        private readonly CompositionEffectBrush  _effectBrush;
        private readonly CompositionDrawingSurface _conicSurface;
        private readonly CompositionDrawingSurface _arcMaskSurface;
        private readonly CompositionDrawingSurface _strokeMaskSurface;
        // Null when the corresponding rotation is frozen (static matrix
        // path) — only the animated paths allocate a PropertySet.
        private readonly CompositionPropertySet? _hueRotationProps;
        private readonly CompositionPropertySet? _arcRotationProps;

        private bool _disposed;

        // `internal` — called from HudComposition.CreateConicArcStroke.
        // C# does not grant the enclosing class access to private members
        // of its nested types (asymmetric: nested → enclosing only), so a
        // `private` ctor here would be uncallable from CreateConicArcStroke.
        // `internal` + `internal ConicArcStrokeConfig` keeps CS0051 happy
        // while not widening the effective API — the struct is still nested.
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
            CompositionPropertySet? arcRotationProps)
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

            CreationId = System.Threading.Interlocked.Increment(ref _creationCounter);
            System.Threading.Interlocked.Increment(ref _liveStrokeCount);
            StrokeLifecycle?.Invoke(CreationId, "created");
        }

        // Blend the live effect properties toward the variant's target
        // values. Called on every HUD state entry into Transcribing or
        // Rewriting, and on live theme change while Transcribing (Exposure
        // is theme-aware). Safe to call repeatedly — each call overrides
        // any in-flight animation and starts fresh from the current value
        // via InsertExpressionKeyFrame("this.CurrentValue").
        public void ApplyVariant(ProcessingVariant variant, bool isDark)
        {
            float sat, hueShiftTurns, exposure, opacity;
            double blendSeconds;

            switch (variant)
            {
                case ProcessingVariant.Recording:
                    // Recording has its own Saturation/Hue/Exposure slots
                    // in the config; defaults mirror Transcribing but
                    // tune independently. Opacity is NOT animated here —
                    // UpdateLevel drives it from EMA-smoothed mic RMS.
                    // See UpdateLevel below.
                    sat           = isDark
                        ? _config.RecordingSaturationDark
                        : _config.RecordingSaturationLight;
                    hueShiftTurns = _config.RecordingHueShiftTurns;
                    exposure      = isDark
                        ? _config.RecordingExposureDark
                        : _config.RecordingExposureLight;
                    opacity       = 0f;                 // unused, skipped below
                    blendSeconds  = _config.RecordingBlendSeconds;
                    break;
                case ProcessingVariant.Transcribing:
                    sat           = isDark
                        ? _config.TranscribingSaturationDark
                        : _config.TranscribingSaturationLight;
                    hueShiftTurns = _config.TranscribingHueShiftTurns;
                    exposure      = isDark
                        ? _config.TranscribingExposureDark
                        : _config.TranscribingExposureLight;
                    opacity       = _config.TranscribingOpacity;
                    blendSeconds  = _config.TranscribingBlendSeconds;
                    break;
                case ProcessingVariant.Rewriting:
                    sat           = _config.RewritingSaturation;
                    hueShiftTurns = _config.RewritingHueShiftTurns;
                    exposure      = _config.RewritingExposure;
                    opacity       = _config.RewritingOpacity;
                    blendSeconds  = _config.RewritingBlendSeconds;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(variant));
            }

            var duration = TimeSpan.FromSeconds(Math.Max(0.001, blendSeconds));
            float hueAngleRadians = hueShiftTurns * MathF.Tau;

            AnimateScalar(_effectProps,  "Saturation", sat,             duration);
            AnimateScalar(_effectProps,  "HueAngle",   hueAngleRadians, duration);
            AnimateScalar(_effectProps,  "Exposure",   exposure,        duration);

            // Recording's Opacity is RMS-driven via UpdateLevel — leave it
            // untouched so an ApplyVariant (e.g. live theme change mid-
            // recording) doesn't knock the outline back to silence.
            if (variant != ProcessingVariant.Recording)
                AnimateScalar(_strokeVisual, "Opacity", opacity, duration);
        }

        // Push a new target opacity in [0, 1]. Called from HudChrono's
        // UpdateAudioLevel on the recording audio thread — CompositionPropertySet
        // and StartAnimation are thread-safe per Composition's contract,
        // no DispatcherQueue marshalling.
        //
        // 50 ms linear key-frame from the current value to the target.
        // InsertExpressionKeyFrame("this.CurrentValue") makes successive
        // overlapping calls blend naturally from wherever the previous
        // animation had reached — no reset to 0, no step discontinuity.
        // The Composition renderthread (vsynced to the monitor refresh —
        // 60/120/144/240 Hz) interpolates between 20 Hz RMS samples at the
        // native rate with no C#-side tick.
        //
        // Only meaningful on a Recording-variant stroke; calling it on a
        // Transcribing / Rewriting stroke would fight ApplyVariant's opacity
        // animation, so HudChrono gates the call on _currentVariant.
        public void UpdateLevel(float level)
        {
            float clamped = Math.Clamp(level, 0f, 1f);
            var anim = _compositor.CreateScalarKeyFrameAnimation();
            anim.InsertExpressionKeyFrame(0f, "this.CurrentValue");
            anim.InsertKeyFrame(1f, clamped);
            anim.Duration = TimeSpan.FromMilliseconds(50);
            _strokeVisual.StartAnimation("Opacity", anim);
        }

        // "Start from the current value, reach target at the end of
        // duration." InsertExpressionKeyFrame("this.CurrentValue") reads
        // the live value at animation-start, so overlapping calls blend
        // naturally from wherever the previous animation had reached.
        private void AnimateScalar(
            CompositionObject target, string property, float value, TimeSpan duration)
        {
            var anim = _compositor.CreateScalarKeyFrameAnimation();
            anim.InsertExpressionKeyFrame(0f, "this.CurrentValue");
            anim.InsertKeyFrame(1f, value);
            anim.Duration = duration;
            target.StartAnimation(property, anim);
        }

        // Two-phase teardown so no dangling animation fires on a freed
        // resource. Ordering below is intentional:
        //   1. Stop animations at their source (brushes, propertysets,
        //      strokeVisual). StopAnimation is a no-op when no animation
        //      is attached to the property — safe to blanket-call.
        //   2. Dispose native resources in reverse creation order:
        //      effects → brushes → surfaces → propertysets → visuals.
        //      Each Dispose() releases the native handle; managed wrappers
        //      are then GC'd whenever. A missed Dispose here means the
        //      native handle lingers — the freeze-after-N-rebuilds symptom.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // ── 1. Stop all animations ───────────────────────────────
            //
            // Two layers of animations hang on this stroke:
            //
            //   a) ScalarKeyFrameAnimations on the PropertySets and the
            //      strokeVisual — these are the "what's currently moving"
            //      side. Stopping them freezes the value.
            //
            //   b) ExpressionAnimations bound to `_effectBrush` via
            //      BindEffectProperty — three of them, one per effect slot
            //      (Sat.Saturation / Hue.Angle / Exp.Exposure). Each holds
            //      a *reference* to `_effectProps` via SetReferenceParameter.
            //
            // If we only stop (a) and dispose `_effectProps`, the three
            // ExpressionAnimations on effectBrush are still bound to a
            // freed PropertySet. Disposing effectBrush then releases the
            // expressions — BUT between _effectProps.Dispose() and
            // effectBrush.Dispose(), a render tick can evaluate the
            // expression, hit the freed PropertySet, and crash. The
            // Transcribing Exposure slider crash reproduces this exact
            // race: slider → rebuild → dispose-in-flight → render tick
            // reads a half-disposed graph.
            //
            // Fix: explicitly StopAnimation on the effectBrush's animated
            // property paths *before* disposing _effectProps. Each call
            // severs the expression binding on the native side.
            try { _effectBrush.StopAnimation("Sat.Saturation"); } catch { }
            try { _effectBrush.StopAnimation("Hue.Angle");      } catch { }
            try { _effectBrush.StopAnimation("Exp.Exposure");   } catch { }

            try { _conicBrush.StopAnimation("TransformMatrix");   } catch { }
            try { _arcMaskBrush.StopAnimation("TransformMatrix"); } catch { }
            try { _strokeVisual.StopAnimation("Opacity");         } catch { }

            try { _effectProps.StopAnimation("Saturation"); } catch { }
            try { _effectProps.StopAnimation("HueAngle");   } catch { }
            try { _effectProps.StopAnimation("Exposure");   } catch { }

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
            // Effect brush first — it holds the ExpressionAnimations that
            // reference _effectProps. Disposing the brush releases those
            // bindings on the native side. Only then is it safe to dispose
            // _effectProps.
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

    // Processing stroke — single rainbow double-comet shared by the
    // Transcribing and Rewriting states. The returned ProcessingStroke
    // exposes ApplyVariant(…) for the live state blend.
    //
    // Struct defaults on ConicArcStrokeConfig ARE the whole config.
}

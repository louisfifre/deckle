using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Deckle.Audio;
using Deckle.Composition;
using Deckle.Diagnostics;

namespace Deckle.Hud;

// HudChrono — the Composition processing stroke.
//
// ProcessingSurfaceHost (XAML Border) is the attach point for the Composition
// visual produced by HudComposition. The visual sits above ChronoCard and below
// the ClockText panel in the Grid z-order, so its stroke paints on the card
// surface but the clock reads on top.
//
// Fallback dims (272, 78) catch the pre-layout attach (ActualWidth/Height are 0
// before the first measure pass). The visual is not auto-resized on subsequent
// layout passes — acceptable here because Charging/Recording always reset the
// surface, and Transcribing/Rewriting only fire after at least one full chrono
// measure.
//
// The stroke is created on first entry into Recording / Transcribing /
// Rewriting. For Transcribing ↔ Rewriting, ApplyVariant blends effect
// properties on the SAME visual, with no surface rebuild. The Recording ↔
// (Transcribing / Rewriting) boundary crosses between a rotation-frozen stroke
// and a spinning one: the two rotation modes are baked at creation (static
// TransformMatrix vs KeyFrameAnimation on CompositionSurfaceBrush.TransformMatrix,
// impossible to unbind cleanly mid-life), so that crossing disposes and rebuilds.
public sealed partial class HudChrono
{
    private HudComposition.ProcessingStroke? _processingStroke;
    private ProcessingVariant? _currentVariant;

    // Serializes the ProcessingStroke lifecycle (create / Dispose on the UI
    // thread) against the RMS push (UpdateAudioLevel, on the recording audio
    // thread). The push itself is thread-safe per Composition's contract, but
    // its guard, _currentVariant == Recording, is check-then-act across the two
    // threads: without the lock the UI thread can Dispose the stroke mid-pump,
    // leaving UpdateLevel writing to a destroyed visual (native AV, no managed
    // trace). The Recording→Transcribing switch can fire while capture is still
    // draining. The lock is held only briefly during a state switch.
    private readonly object _strokeSync = new();

    private long _processingStrokeAcquiredTicks;
    private long _processingStrokeHandle;

    // Playground-only one-shot config override, consumed by the next stroke
    // creation so a state comes up with its tuned config in a single stroke
    // creation. Null in shipping Deckle, which builds from HudSettingsService.
    private HudComposition.ConicArcStrokeConfig? _nextStrokeConfig;

    // The config the stroke on screen was built from, whatever it came from:
    // the gauge paints on the same outline, so it reads this rather than the
    // store, and the two can never disagree. Written under _strokeSync and
    // read on the UI thread only (RebuildCountdownVisual).
    private HudComposition.ConicArcStrokeConfig? _strokeConfig;

    // The mapping and its tunables live in Deckle.Audio.AudioLevelMapper; the
    // smoother state is per-consumer, so it stays here.
    private float _smoothedLevel;

    // Called from the recording audio thread. Gated on _currentVariant ==
    // Recording so the engine event can stay subscribed permanently:
    // Transcribing / Rewriting strokes have ApplyVariant-driven opacity and
    // must not be pushed from the RMS pump. CompositionPropertySet +
    // StartAnimation are thread-safe per Composition's contract, so no
    // DispatcherQueue.
    public void UpdateAudioLevel(float rms)
    {
        // Held for the whole read-and-write so a UI-thread Dispose can never
        // land between the guard and UpdateLevel — see _strokeSync.
        lock (_strokeSync)
        {
            if (_processingStroke is null) return;
            if (_currentVariant != ProcessingVariant.Recording) return;

            float perceptual = AudioLevelMapper.RmsToPerceptualLevel(rms);
            float a = AudioLevelMapper.EmaAlpha;
            _smoothedLevel = _smoothedLevel * a + perceptual * (1f - a);
            _processingStroke.UpdateLevel(_smoothedLevel);
        }
    }

    private void AttachProcessingVisual(ProcessingVariant variant)
    {
        bool isDark = ChronoRoot.ActualTheme == ElementTheme.Dark;

        // Whole mutation region under _strokeSync: the audio thread reads
        // _processingStroke / _currentVariant in UpdateAudioLevel, so the
        // Dispose, the rebuild, and the _currentVariant flip must be atomic
        // against it, otherwise the pump can write to a half-swapped or
        // destroyed stroke.
        lock (_strokeSync)
        {
            bool crossingBoundary =
                _processingStroke != null &&
                IsRecording(variant) != IsRecording(_currentVariant);

            if (crossingBoundary)
            {
                ElementCompositionPreview.SetElementChildVisual(ProcessingSurfaceHost, null);
                int ageMsBoundary = (int)((Stopwatch.GetTimestamp() - _processingStrokeAcquiredTicks)
                                           * 1000L / Stopwatch.Frequency);
                DeckleResourceSource.Log.ResourceReleased(
                    "composition-visual", _processingStrokeHandle, ageMsBoundary, "hud-chrono-stroke");
                _processingStroke!.Dispose();
                _processingStroke = null;
            }

            if (_processingStroke == null)
            {
                var compositor = ElementCompositionPreview
                    .GetElementVisual(ProcessingSurfaceHost).Compositor;

                float w = (float)ProcessingSurfaceHost.ActualWidth;
                float h = (float)ProcessingSurfaceHost.ActualHeight;
                if (w == 0f || h == 0f) { w = 272f; h = 78f; }
                var size = new Vector2(w, h);

                var cfg = _nextStrokeConfig ?? HudSettingsService.Instance.EffectiveStroke();
                _nextStrokeConfig = null;
                _strokeConfig = cfg;

                _processingStroke = variant == ProcessingVariant.Recording
                    ? HudComposition.CreateRecordingStroke(compositor, size, cfg, _animationsEnabled)
                    : HudComposition.CreateProcessingStroke(compositor, size, cfg, _animationsEnabled);
                ElementCompositionPreview.SetElementChildVisual(
                    ProcessingSurfaceHost, _processingStroke.Visual);

                // size_bytes is 0: a Composition Visual's memory size is not
                // measurable from managed code without costly introspection.
                _processingStrokeHandle = RuntimeHelpers.GetHashCode(_processingStroke.Visual);
                _processingStrokeAcquiredTicks = Stopwatch.GetTimestamp();
                DeckleResourceSource.Log.ResourceAcquired(
                    "composition-visual", _processingStrokeHandle, 0, "hud-chrono-stroke");
            }

            // Reset the EMA accumulator on every Recording entry so leftover
            // energy from a previous recording doesn't seed the new outline
            // with a non-zero opacity floor.
            if (variant == ProcessingVariant.Recording)
                _smoothedLevel = 0f;

            _currentVariant = variant;

            // ApplyVariant skips Opacity for Recording: UpdateLevel owns that
            // channel.
            _processingStroke.ApplyVariant(variant, isDark);
        }
    }

    private void DetachProcessingVisual()
    {
        // Same _strokeSync discipline as AttachProcessingVisual — the final
        // teardown disposes the stroke the audio pump may still be reading.
        lock (_strokeSync)
        {
            if (_processingStroke == null) return;

            ElementCompositionPreview.SetElementChildVisual(ProcessingSurfaceHost, null);
            int ageMs = (int)((Stopwatch.GetTimestamp() - _processingStrokeAcquiredTicks)
                               * 1000L / Stopwatch.Frequency);
            DeckleResourceSource.Log.ResourceReleased(
                "composition-visual", _processingStrokeHandle, ageMs, "hud-chrono-stroke");
            _processingStroke.Dispose();
            _processingStroke = null;
            _strokeConfig     = null;
            _currentVariant   = null;
        }
    }

    private static bool IsRecording(ProcessingVariant? v)
        => v == ProcessingVariant.Recording;

    // One-shot: must be armed before the ApplyState that creates the stroke,
    // and is cleared as soon as it is used.
    public void SetNextStrokeConfig(HudComposition.ConicArcStrokeConfig config)
    {
        _nextStrokeConfig = config;
    }

    // Rebuilt, not mutated: paint-time fields are baked into Win2D surfaces
    // and cannot be animated live. The config must match the current variant
    // (Recording* fields apply only to Recording).
    public void RebuildStroke(
        HudComposition.ConicArcStrokeConfig config,
        System.Action<string, string>? log = null)
    {
        if (_currentVariant is not { } variant)
        {
            log?.Invoke("REBUILD", "skip — no active variant");
            return;
        }

        try
        {
            log?.Invoke("REBUILD", $"begin variant={variant}");

            ElementCompositionPreview.SetElementChildVisual(ProcessingSurfaceHost, null);
            log?.Invoke("REBUILD", "detached old visual from host");

            if (_processingStroke != null)
            {
                int ageMsRebuild = (int)((Stopwatch.GetTimestamp() - _processingStrokeAcquiredTicks)
                                          * 1000L / Stopwatch.Frequency);
                DeckleResourceSource.Log.ResourceReleased(
                    "composition-visual", _processingStrokeHandle, ageMsRebuild, "hud-chrono-stroke");
            }
            _processingStroke?.Dispose();
            _processingStroke = null;
            log?.Invoke("REBUILD", "disposed old ProcessingStroke");

            var compositor = ElementCompositionPreview
                .GetElementVisual(ProcessingSurfaceHost).Compositor;

            float w = (float)ProcessingSurfaceHost.ActualWidth;
            float h = (float)ProcessingSurfaceHost.ActualHeight;
            bool fallback = (w == 0f || h == 0f);
            if (fallback) { w = 272f; h = 78f; }
            var size = new Vector2(w, h);
            log?.Invoke("REBUILD", $"size={w:F1}×{h:F1}{(fallback ? " (fallback)" : "")}");

            _processingStroke = variant == ProcessingVariant.Recording
                ? HudComposition.CreateRecordingStroke(compositor, size, config, _animationsEnabled)
                : HudComposition.CreateProcessingStroke(compositor, size, config, _animationsEnabled);
            _strokeConfig = config;
            // A live count climbing above 1 means a Dispose path is leaking.
            log?.Invoke("REBUILD",
                $"created {(variant == ProcessingVariant.Recording ? "Recording" : "Processing")}Stroke " +
                $"#{_processingStroke.CreationId} (live={HudComposition.LiveStrokeCount})");

            ElementCompositionPreview.SetElementChildVisual(
                ProcessingSurfaceHost, _processingStroke.Visual);
            log?.Invoke("REBUILD", "attached new visual to host");

            _processingStrokeHandle = RuntimeHelpers.GetHashCode(_processingStroke.Visual);
            _processingStrokeAcquiredTicks = Stopwatch.GetTimestamp();
            DeckleResourceSource.Log.ResourceAcquired(
                "composition-visual", _processingStrokeHandle, 0, "hud-chrono-stroke");

            bool isDark = ChronoRoot.ActualTheme == ElementTheme.Dark;
            _processingStroke.ApplyVariant(variant, isDark);
            log?.Invoke("REBUILD", $"ApplyVariant {variant} isDark={isDark} — done");
        }
        catch (System.Exception ex)
        {
            // Composition can throw here (device lost, surface creation,
            // expression parse in StartRotation). Log it before rethrowing:
            // otherwise the failure can show only as frozen animations.
            log?.Invoke("ERROR", $"RebuildStroke threw {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}

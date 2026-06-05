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
// the ClockText in the Grid z-order, so its stroke paints on the card surface
// but the clock text reads on top.
//
// Fallback dims (272, 78) catch the pre-layout attach (ActualWidth/Height are 0
// before the first measure pass). The visual is not auto-resized on subsequent
// layout passes — acceptable here because Charging/Recording always reset the
// surface, and Transcribing/Rewriting only fire after at least one full chrono
// measure.
//
// Single-pipeline, live-modulated variants where possible. The stroke is
// created on first enter into Recording / Transcribing / Rewriting. For
// Transcribing ↔ Rewriting transitions, ApplyVariant blends effect properties
// on the SAME visual — no surface rebuild. The Recording ↔ (Transcribing /
// Rewriting) boundary crosses between a rotation-frozen stroke and a spinning
// one: the two rotation modes are baked at creation (static TransformMatrix vs
// KeyFrameAnimation on CompositionSurfaceBrush.TransformMatrix, settled once and
// impossible to unbind cleanly mid-life), so we dispose and rebuild.
public sealed partial class HudChrono
{
    private HudComposition.ProcessingStroke? _processingStroke;
    private ProcessingVariant? _currentVariant;

    // Serializes the ProcessingStroke lifecycle (create / Dispose, owned by
    // the UI thread in Attach/DetachProcessingVisual) against the ~20 Hz RMS
    // push (UpdateAudioLevel, on the recording audio thread). The push itself
    // is documented thread-safe per Composition's contract, but the gate it
    // relies on — _currentVariant == Recording — was check-then-act across the
    // two threads: the audio thread could clear both guards, then the UI
    // thread Dispose() the stroke mid-pump, leaving UpdateLevel writing to a
    // destroyed visual (native AV, no managed trace). This only became
    // reachable once the Recording→Transcribing switch could fire while
    // capture was still draining (instant Stop acknowledgement); until then
    // the switch always ran after capture had quiesced. The lock is
    // uncontended at 20 Hz and only ever briefly held during a state switch.
    private readonly object _strokeSync = new();

    // Acquire timestamp + frozen handle for the current ProcessingStroke:
    // captured in AttachProcessingVisual, read in Dispose to compute age_ms on
    // the DeckleResourceSource side. The managed handle remains valid after
    // dispose to identify the released event (the stroke itself was replaced
    // with null).
    private long _processingStrokeAcquiredTicks;
    private long _processingStrokeHandle;

    // HudPlayground-only: config override consumed (and cleared) by the
    // next stroke creation inside AttachProcessingVisual. Lets the
    // playground bring up a state with a caller-supplied config in a
    // single stroke creation — without this slot, the playground had to
    // call ApplyState (creates a stroke with shipping defaults) then
    // RebuildStroke (dispose + recreate with tuning config), which
    // doubled the stroke churn on every target change and inflated
    // live_stroke_count artefacts in the instrumentation log.
    //
    // Null in shipping Deckle — OnLaunched and HudWindow never set it,
    // so AttachProcessingVisual falls back to the factories' default
    // configs. Shipping behaviour is byte-identical.
    private HudComposition.ConicArcStrokeConfig? _nextStrokeConfig;

    // EMA-smoothed perceptual level. The mapping math + the four
    // tunables (EmaAlpha / MinDbfs / MaxDbfs / DbfsCurveExponent) live
    // in Deckle.Audio.AudioLevelMapper — they're audio-domain, not
    // HUD-domain. The smoother STATE is per-consumer though, so it
    // stays here.
    private float _smoothedLevel;

    // Forwarded from HudWindow.OnAudioLevel. Called from the recording
    // audio thread. Gated on _currentVariant == Recording so the engine
    // event can stay subscribed permanently — Transcribing / Rewriting
    // strokes have ApplyVariant-driven opacity and must not be pushed
    // from the RMS pump. CompositionPropertySet + StartAnimation are
    // thread-safe per Composition's contract — no DispatcherQueue.
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

        // Rotation-frozen vs spinning strokes cannot share a SpriteVisual —
        // the TransformMatrix is set once at creation (static matrix or
        // keyframe animation) and swapping modes live isn't supported by
        // Composition. Tear the existing stroke down when crossing that
        // boundary; in-kind transitions (Transcribing ↔ Rewriting) keep
        // the same visual and only blend effect properties.
        // Whole mutation region under _strokeSync: the audio thread reads
        // _processingStroke / _currentVariant in UpdateAudioLevel, so the
        // Dispose, the rebuild, and the _currentVariant flip must be atomic
        // against it — otherwise the pump can write to a half-swapped or
        // destroyed stroke. UI-thread + infrequent, so holding it across the
        // Composition calls is free in practice.
        lock (_strokeSync)
        {
            bool crossingBoundary =
                _processingStroke != null &&
                IsRecording(variant) != IsRecording(_currentVariant);

            if (crossingBoundary)
            {
                ElementCompositionPreview.SetElementChildVisual(ProcessingSurfaceHost, null);
                // Cross-cutting Resource sub-provider: stroke release at the
                // boundary-crossing dispose point (Recording ↔ Processing).
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

                // Consume the one-shot config override if the playground armed
                // one before this ApplyState call. Null in shipping Deckle.
                var cfg = _nextStrokeConfig;
                _nextStrokeConfig = null;

                _processingStroke = variant == ProcessingVariant.Recording
                    ? HudComposition.CreateRecordingStroke(compositor, size, cfg)
                    : HudComposition.CreateProcessingStroke(compositor, size, cfg);
                ElementCompositionPreview.SetElementChildVisual(
                    ProcessingSurfaceHost, _processingStroke.Visual);

                // Cross-cutting Resource sub-provider: stroke acquire.
                // Handle = RuntimeHelpers.GetHashCode of the managed Visual
                // (stable for the object's lifetime). size_bytes=0: the memory
                // size of a Composition Visual is not measurable from managed code
                // without costly introspection, per the provider convention.
                _processingStrokeHandle = RuntimeHelpers.GetHashCode(_processingStroke.Visual);
                _processingStrokeAcquiredTicks = Stopwatch.GetTimestamp();
                DeckleResourceSource.Log.ResourceAcquired(
                    "composition-visual", _processingStrokeHandle, 0, "hud-chrono-stroke");
            }

            // Reset the EMA accumulator on every Recording entry so leftover
            // energy from a previous recording session doesn't seed the new
            // outline with a non-zero opacity floor. Safe to reset here even
            // on a same-kind re-attach (ApplyRecording → ApplyRecording) — the
            // Recording path always starts from silence.
            if (variant == ProcessingVariant.Recording)
                _smoothedLevel = 0f;

            _currentVariant = variant;

            // Cold start or in-kind transition: blend the effect properties to
            // the new variant's targets. ApplyVariant skips Opacity for
            // Recording (UpdateLevel owns that channel).
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
            // Cross-cutting Resource sub-provider: release the stroke during final
            // teardown (Hidden state).
            int ageMs = (int)((Stopwatch.GetTimestamp() - _processingStrokeAcquiredTicks)
                               * 1000L / Stopwatch.Frequency);
            DeckleResourceSource.Log.ResourceReleased(
                "composition-visual", _processingStrokeHandle, ageMs, "hud-chrono-stroke");
            _processingStroke.Dispose();
            _processingStroke = null;
            _currentVariant   = null;
        }
    }

    private static bool IsRecording(ProcessingVariant? v)
        => v == ProcessingVariant.Recording;

    // HudPlayground-only: arms a config override to be consumed by the
    // very next stroke creation inside AttachProcessingVisual (which
    // happens during ApplyState for Recording / Transcribing / Rewriting).
    // The override is one-shot — it's cleared as soon as it's used, so
    // subsequent ApplyState calls without a fresh Set* would fall back to
    // the factories' defaults. Use RebuildStroke after the state is live
    // to apply a new config without changing state.
    public void SetNextStrokeConfig(HudComposition.ConicArcStrokeConfig config)
    {
        _nextStrokeConfig = config;
    }

    // HudPlayground-only: rebuild the stroke with a caller-supplied config
    // so baked-geometry knobs (StrokeThickness, WedgeCount, ConicSpan*,
    // ArcMirror, ArcPhaseTurns, etc.) can be explored interactively. The
    // stroke is rebuilt, not mutated — paint-time fields are baked into
    // Win2D surfaces and cannot be animated live.
    //
    // No-op when no variant is active (Hidden / Charging have no stroke).
    // The current variant determines which factory is called; the caller
    // must pass a config that matches (Recording* fields honoured when
    // variant == Recording, generic fields otherwise).
    // `log` is an optional diagnostic sink used exclusively by the
    // HudPlayground — shipping Deckle never passes one, so the null-
    // conditional invocations collapse to zero cost. Each anchor lets
    // the playground log panel show the exact lifecycle order when
    // a stroke is observed freezing mid-run; the try/catch wraps
    // the whole teardown + rebuild + apply sequence so a Composition
    // exception thrown deep inside any of the factories surfaces as a
    // visible ERROR line instead of freezing silently.
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

            // Cross-cutting Resource sub-provider: release the stroke before
            // dispose in the Playground RebuildStroke path.
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
                ? HudComposition.CreateRecordingStroke(compositor, size, config)
                : HudComposition.CreateProcessingStroke(compositor, size, config);
            // Log the unique CreationId + live-count alongside the variant
            // so the playground log reads chronologically as "created #N
            // (live=K)" — when K starts climbing beyond 1, the Dispose
            // path is failing somewhere and that's the freeze signal.
            log?.Invoke("REBUILD",
                $"created {(variant == ProcessingVariant.Recording ? "Recording" : "Processing")}Stroke " +
                $"#{_processingStroke.CreationId} (live={HudComposition.LiveStrokeCount})");

            ElementCompositionPreview.SetElementChildVisual(
                ProcessingSurfaceHost, _processingStroke.Visual);
            log?.Invoke("REBUILD", "attached new visual to host");

            // Cross-cutting Resource sub-provider: acquire the new stroke
            // in the Playground RebuildStroke path.
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
            // Composition can throw (e.g. DirectX device lost, surface
            // creation failure, expression parse error in StartRotation).
            // Without this catch the exception bubbles to the UI thread
            // and either crashes the playground or — worse — silently
            // kills the visual's animations, which is exactly the
            // "freeze mid-run" symptom we are tracking down.
            log?.Invoke("ERROR", $"RebuildStroke threw {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}

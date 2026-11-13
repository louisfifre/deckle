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

    // The outline's look is the compiled defaults, nothing else: the stroke,
    // the gauge and the digit reveal all paint from this one config, so they
    // cannot disagree. A modules\hud\settings.json left by an older Deckle is
    // never read.
    private static readonly HudComposition.ConicArcStrokeConfig StrokeConfig = new();

    // The mapping and its tunables live in Deckle.Audio.AudioLevelMapper; the
    // smoother state is per-consumer, so it stays here.
    private float _smoothedLevel;

    // A composition that refuses the stroke usually refuses it on every state
    // change until the device comes back: one Warning opens the incident, the
    // next stroke built closes it. UI thread only.
    private bool _strokeIncidentOpen;
    private int  _strokeFailures;

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
            try
            {
                AttachProcessingVisualLocked(variant, isDark);
                NoteStrokeBuilt();
            }
            catch (System.Exception ex)
            {
                // Composition can throw here (device lost, surface
                // creation, expression parse). Recorded before
                // the rethrow, which keeps the caller's existing handling: a
                // failure that only surfaced as a missing stroke left no trace.
                NoteStrokeFailure("attach", variant, ex);
                throw;
            }
        }
    }

    // Caller holds _strokeSync.
    private void AttachProcessingVisualLocked(ProcessingVariant variant, bool isDark)
    {
        // The variant the new stroke replaces, read before the flip:
        // after Recording the processing stroke blends in from it.
        var previous = _currentVariant;
        bool crossingBoundary =
            _processingStroke != null &&
            IsRecording(variant) != IsRecording(previous);

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

            _processingStroke = variant == ProcessingVariant.Recording
                ? HudComposition.CreateRecordingStroke(compositor, size, isDark, StrokeConfig, _animationsEnabled)
                : HudComposition.CreateProcessingStroke(compositor, size, isDark, StrokeConfig, _animationsEnabled,
                                                        from: crossingBoundary ? previous : null);
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
            _currentVariant   = null;
        }
    }

    private static bool IsRecording(ProcessingVariant? v)
        => v == ProcessingVariant.Recording;

    private void NoteStrokeFailure(string phase, ProcessingVariant variant, System.Exception ex)
    {
        _strokeFailures++;
        string variantName = variant.ToString();
        string exType = ex.GetType().Name;
        DeckleHudSource.Log.StrokeCreationAttemptFailed(phase, variantName, exType, ex.Message);

        if (_strokeIncidentOpen) return;
        _strokeIncidentOpen = true;
        DeckleHudSource.Log.StrokeCreationFailed(phase, variantName, exType, ex.Message);
    }

    private void NoteStrokeBuilt()
    {
        if (!_strokeIncidentOpen) return;
        _strokeIncidentOpen = false;
        DeckleHudSource.Log.StrokeCreationRecovered(_strokeFailures);
        _strokeFailures = 0;
    }
}

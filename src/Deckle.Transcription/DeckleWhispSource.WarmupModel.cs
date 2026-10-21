using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Warmup clip ──────────────────────────────────────────────────────

    // The clip ships with the app, so its absence is an incomplete install or a
    // build without its assets. The warm-up then runs on silence, the input on
    // which Whisper decodes subtitle credits: a reader must see it.
    [Event(EvtWarmupClipMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The warmup clip is missing, the model warms up on silence")]
    public void WarmupClipMissing()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipMissing);
    }

    [Event(EvtWarmupClipMissingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup clip missing | path={0}")]
    public void WarmupClipMissingDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipMissingDetail, path);
    }

    [Event(EvtWarmupClipHeaderInvalid,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The warmup clip has an invalid header")]
    public void WarmupClipHeaderInvalid()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipHeaderInvalid);
    }

    [Event(EvtWarmupClipHeaderInvalidDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup clip header invalid | path={0}")]
    public void WarmupClipHeaderInvalidDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipHeaderInvalidDetail, path);
    }

    [Event(EvtWarmupClipSampleMismatch,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The warmup clip is not in the expected audio format")]
    public void WarmupClipSampleMismatch()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipSampleMismatch);
    }

    [Event(EvtWarmupClipSampleMismatchDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup clip format unexpected | format={0} | channels={1} | sample_rate={2} | bits={3} | expected=PCM mono 16-bit 16 kHz")]
    public void WarmupClipSampleMismatchDetail(int audio_format, int num_channels, int sample_rate, int bits_per_sample)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipSampleMismatchDetail, audio_format, num_channels, sample_rate, bits_per_sample);
    }

    [Event(EvtWarmupClipLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The warmup clip could not be loaded, the model warms up on silence | ex_type={0} | ex_message={1}")]
    public void WarmupClipLoadFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipLoadFailed, ex_type, ex_message);
    }

    // ── Warmup pipeline ──────────────────────────────────────────────────
    //
    // WarmupStart always ends with WarmupComplete, WarmupCancelled or
    // WarmupFailed: EnsurePrimed emits one on every exit, thrown ones included.

    [Event(EvtWarmupStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup start")]
    public void WarmupStart()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupStart);
    }

    [Event(EvtWarmupComplete,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup complete")]
    public void WarmupComplete()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupComplete);
    }

    // A Stop or Dispose that lands during the prime; the next take primes again.
    [Event(EvtWarmupCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup cancelled | elapsed_ms={0}")]
    public void WarmupCancelled(long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupCancelled, elapsed_ms);
    }

    // reason (closed vocabulary): model_load_failed, whose cause the
    // ModelUnavailable incident already carries (ex_type empty), or
    // prime_exception, whose exception propagates to the worker afterwards.
    [Event(EvtWarmupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup failed | reason={0} | ex_type={1} | ex_message={2} | elapsed_ms={3}")]
    public void WarmupFailed(string reason, string ex_type, string ex_message, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupFailed, reason, ex_type, ex_message, elapsed_ms);
    }

    // Cold prime only: prime_ms is the model load plus dummy inference,
    // gate_wait_ms how long the first real backend call waited for it, and
    // gate_phase (closed vocabulary) where that wait sat, which sets its meaning:
    //   at_stop          (monolithic) — post-Stop latency the user perceived;
    //                     ≈ 0 means the recording hid the whole cold cost.
    //   during_recording (segmented)  — the consumer waited while capture ran;
    //                     a large value is hidden cost, not user-facing latency.
    [Event(EvtPrimeOverlap,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "prime overlap | prime_ms={0} | gate_wait_ms={1} | gate_phase={2}")]
    public void PrimeOverlap(long prime_ms, long gate_wait_ms, string gate_phase)
    {
        if (IsEnabled()) WriteEvent(EvtPrimeOverlap, prime_ms, gate_wait_ms, gate_phase);
    }

    // ── Model lifecycle ─────────────────────────────────────────────────

    [Event(EvtModelLoading,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Loading model")]
    public void ModelLoading()
    {
        if (IsEnabled()) WriteEvent(EvtModelLoading);
    }

    [Event(EvtModelInitFromFile,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "whisper_init_from_file returned ctx={0}")]
    public void ModelInitFromFile(long ctx)
    {
        if (IsEnabled()) WriteEvent(EvtModelInitFromFile, ctx);
    }

    // Opens the "no transcription" incident, once until a load succeeds.
    // reason is the backend's ErrorReason, load_failed when it gave none, or
    // load_exception when the load threw; ex_type and ex_message are empty
    // unless it threw. The backend's own ModelLoadFailed carries the device.
    [Event(EvtModelUnavailable,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The transcription model is unavailable | reason={0} | ex_type={1} | ex_message={2}")]
    public void ModelUnavailable(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtModelUnavailable, reason, ex_type, ex_message);
    }

    // A later attempt that fails while the incident is open.
    [Event(EvtModelLoadRetryFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "model load retry failed | reason={0} | ex_type={1} | ex_message={2}")]
    public void ModelLoadRetryFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadRetryFailed, reason, ex_type, ex_message);
    }

    [Event(EvtModelRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Transcription model availability recovered")]
    public void ModelRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtModelRecovered);
    }

    [Event(EvtModelLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Model loaded")]
    public void ModelLoaded()
    {
        if (IsEnabled()) WriteEvent(EvtModelLoaded);
    }

    [Event(EvtModelOnDemandLoad,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "on-demand load | reason=first_use_or_after_idle_unload")]
    public void ModelOnDemandLoad()
    {
        if (IsEnabled()) WriteEvent(EvtModelOnDemandLoad);
    }

    [Event(EvtModelIdleUnloadSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "idle unload skipped | state={0}")]
    public void ModelIdleUnloadSkipped(string state)
    {
        if (IsEnabled()) WriteEvent(EvtModelIdleUnloadSkipped, state);
    }

    // The idle-unload timer runs on the thread pool, where an escaping
    // exception ends the process; the model then stays loaded until the next
    // take re-arms the timer.
    [Event(EvtModelIdleUnloadFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The idle model unload failed, the model stays loaded | ex_type={0} | ex_message={1}")]
    public void ModelIdleUnloadFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtModelIdleUnloadFailed, ex_type, ex_message);
    }

    [Event(EvtModelUnloadedJalon,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Model unloaded")]
    public void ModelUnloadedJalon()
    {
        if (IsEnabled()) WriteEvent(EvtModelUnloadedJalon);
    }

    [Event(EvtModelUnloaded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "model unloaded | idle_s={0} | state=vram-freed")]
    public void ModelUnloaded(int idle_s)
    {
        if (IsEnabled()) WriteEvent(EvtModelUnloaded, idle_s);
    }

    [Event(EvtModelIdleTimerSet,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "idle timer set | idle_s={0}")]
    public void ModelIdleTimerSet(int idle_s)
    {
        if (IsEnabled()) WriteEvent(EvtModelIdleTimerSet, idle_s);
    }

    [Event(EvtModelPathEnvIgnored,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The model path environment variable was ignored and the default was used")]
    public void ModelPathEnvIgnored()
    {
        if (IsEnabled()) WriteEvent(EvtModelPathEnvIgnored);
    }

    [Event(EvtModelPathEnvIgnoredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "model path env ignored | reason=not_an_existing_absolute_path | env_path={0} | fallback={1}")]
    public void ModelPathEnvIgnoredDetail(string env_path, string fallback)
    {
        if (IsEnabled()) WriteEvent(EvtModelPathEnvIgnoredDetail, env_path, fallback);
    }

    [Event(EvtModelFallback,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The configured model is not on disk, an installed one is used instead")]
    public void ModelFallback()
    {
        if (IsEnabled()) WriteEvent(EvtModelFallback);
    }

    [Event(EvtModelFallbackDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "model fallback | reason=configured_file_absent | configured={0} | installed={1}")]
    public void ModelFallbackDetail(string configured, string installed)
    {
        if (IsEnabled()) WriteEvent(EvtModelFallbackDetail, configured, installed);
    }

    // ── Whisper init-phase compaction ────────────────────────────────────
    //
    // Each event consolidates one phase of whisper.cpp's init flow that
    // would otherwise spam several separate Verbose lines. The summary
    // payload is built by WhisperNativeLogCompactor from the per-phase
    // lines as they arrive; flush happens on the first line of the next
    // phase (or on any non-phase line).

    [Event(EvtWhisperInitParamsParsed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "whisper init params | {0}")]
    public void WhisperInitParamsParsed(string summary)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperInitParamsParsed, summary);
    }

    [Event(EvtWhisperModelLoadParsed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "whisper model load | {0}")]
    public void WhisperModelLoadParsed(string summary)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperModelLoadParsed, summary);
    }

    [Event(EvtWhisperBackendInitParsed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "whisper backend init | {0}")]
    public void WhisperBackendInitParsed(string summary)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperBackendInitParsed, summary);
    }

    [Event(EvtWhisperInitStateParsed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "whisper init state | {0}")]
    public void WhisperInitStateParsed(string summary)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperInitStateParsed, summary);
    }
}

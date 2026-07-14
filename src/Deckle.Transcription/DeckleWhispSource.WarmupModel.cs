using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Warmup clip ──────────────────────────────────────────────────────

    [Event(EvtWarmupClipMissing,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup clip missing | path={0}")]
    public void WarmupClipMissing(string path)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipMissing, path);
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
           Message = "The warmup clip could not be loaded")]
    public void WarmupClipLoadFailed()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipLoadFailed);
    }

    [Event(EvtWarmupClipLoadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup clip load failed | ex_type={0} | ex_message={1}")]
    public void WarmupClipLoadFailedDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipLoadFailedDetail, ex_type, ex_message);
    }

    // ── Warmup pipeline ──────────────────────────────────────────────────

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

    // Structured measure for the concurrent prime (cold worker only): how long the
    // prime's model-load + dummy inference took (prime_ms), how long the first real
    // backend call waited for it at the gate (gate_wait_ms), and WHERE that wait
    // sat (gate_phase, closed vocabulary). The wait's meaning depends on the phase,
    // so the field always reads the same once filtered on it:
    //   at_stop          (monolithic) — post-Stop latency the user actually
    //                     perceived; ≈ 0 means the recording fully hid the cold
    //                     cost, > 0 is the residual because the take was shorter
    //                     than the prime.
    //   during_recording (streaming)  — the consumer began waiting before the first
    //                     utterance was ready, fully overlapped with capture and
    //                     never perceived at Stop; a large value is hidden cost, not
    //                     user-facing latency.
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

    [Event(EvtModelLoadStart,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "load start | file={0} | file_mb={1:F1} | use_gpu=1")]
    public void ModelLoadStart(string file, double file_mb)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadStart, file, file_mb);
    }

    [Event(EvtModelLoadAborted,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Model load was aborted")]
    public void ModelLoadAborted()
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadAborted);
    }

    [Event(EvtModelLoadAbortedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "load aborted | reason={0} | path={1}")]
    public void ModelLoadAbortedDetail(string reason, string path)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadAbortedDetail, reason, path);
    }

    [Event(EvtModelInitFromFile,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "whisper_init_from_file returned ctx={0}")]
    public void ModelInitFromFile(long ctx)
    {
        if (IsEnabled()) WriteEvent(EvtModelInitFromFile, ctx);
    }

    [Event(EvtModelLoadFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The model could not be loaded")]
    public void ModelLoadFailed()
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadFailed);
    }

    // The `path` key carries the model path on the backend init-failure call
    // site and the exception message on the engine catch call site; the key is
    // frozen, so the pre-existing semantic overlap is preserved as-is.
    [Event(EvtModelLoadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "load failed | path={0}")]
    public void ModelLoadFailedDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadFailedDetail, path);
    }

    [Event(EvtModelLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Model loaded")]
    public void ModelLoaded()
    {
        if (IsEnabled()) WriteEvent(EvtModelLoaded);
    }

    [Event(EvtModelLoadedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "model loaded | backend={0}")]
    public void ModelLoadedDetail(string backend)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadedDetail, backend);
    }

    [Event(EvtModelLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "load complete | load_ms={0} | backend={1}")]
    public void ModelLoadComplete(long load_ms, string backend)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadComplete, load_ms, backend);
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
           Message = "idle timer set ({0}s)")]
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

    // ── Whisper.cpp log redirect ────────────────────────────────────────

    [Event(EvtWhisperLogVerbose,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}")]
    public void WhisperLogVerbose(string message)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogVerbose, message);
    }

    // Native-log passthrough: the milestone names the source; the raw
    // whisper.cpp line ({0}, native casing) is mirrored at Verbose.
    [Event(EvtWhisperLogWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Whisper reported a warning")]
    public void WhisperLogWarning()
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogWarning);
    }

    [Event(EvtWhisperLogWarningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}")]
    public void WhisperLogWarningDetail(string message)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogWarningDetail, message);
    }

    [Event(EvtWhisperLogError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Whisper reported an error")]
    public void WhisperLogError()
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogError);
    }

    [Event(EvtWhisperLogErrorDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}")]
    public void WhisperLogErrorDetail(string message)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogErrorDetail, message);
    }

    [Event(EvtWhisperLogSetUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Whisper log hook could not be installed")]
    public void WhisperLogSetUnavailable()
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogSetUnavailable);
    }

    [Event(EvtWhisperLogSetUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "whisper_log_set unavailable | ex_message={0}")]
    public void WhisperLogSetUnavailableDetail(string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogSetUnavailableDetail, ex_message);
    }

    // ── Whisper init-phase compaction ────────────────────────────────────
    //
    // Each event consolidates one phase of whisper.cpp's init flow that
    // would otherwise spam 3 to 17 separate Verbose lines. The summary
    // payload is built by WhisperBackend's log hook from the per-phase
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
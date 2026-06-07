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
           Message = "warmup clip header invalid | path={0}")]
    public void WarmupClipHeaderInvalid(string path)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipHeaderInvalid, path);
    }

    [Event(EvtWarmupClipSampleMismatch,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup clip format unexpected | format={0} ch={1} sr={2} bits={3} (expected PCM mono 16-bit 16 kHz)")]
    public void WarmupClipSampleMismatch(int audio_format, int num_channels, int sample_rate, int bits_per_sample)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipSampleMismatch, audio_format, num_channels, sample_rate, bits_per_sample);
    }

    [Event(EvtWarmupClipLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup clip load failed | error={0}: {1}")]
    public void WarmupClipLoadFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipLoadFailed, ex_type, ex_message);
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
           Message = "load aborted | reason={0} | path={1}")]
    public void ModelLoadAborted(string reason, string path)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadAborted, reason, path);
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
           Message = "load failed | path={0}")]
    public void ModelLoadFailed(string path)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadFailed, path);
    }

    [Event(EvtModelLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Model loaded ({0})")]
    public void ModelLoaded(string backend)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoaded, backend);
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
           Message = "DECKLE_MODEL_PATH ignored (not an existing absolute path): \"{0}\". Falling back to \"{1}\".")]
    public void ModelPathEnvIgnored(string env_path, string fallback)
    {
        if (IsEnabled()) WriteEvent(EvtModelPathEnvIgnored, env_path, fallback);
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

    [Event(EvtWhisperLogWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}")]
    public void WhisperLogWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogWarning, message);
    }

    [Event(EvtWhisperLogError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}")]
    public void WhisperLogError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogError, message);
    }

    [Event(EvtWhisperLogSetUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "whisper_log_set unavailable: {0}")]
    public void WhisperLogSetUnavailable(string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperLogSetUnavailable, ex_message);
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
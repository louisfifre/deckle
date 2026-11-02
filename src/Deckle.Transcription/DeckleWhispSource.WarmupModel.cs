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
           Message = "path={0}")]
    public void WarmupClipMissingDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipMissingDetail, path);
    }

    [Event(EvtWarmupClipHeaderInvalid,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The warmup clip has an invalid header, so the model warms up on silence")]
    public void WarmupClipHeaderInvalid()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipHeaderInvalid);
    }

    [Event(EvtWarmupClipHeaderInvalidDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0}")]
    public void WarmupClipHeaderInvalidDetail(string path)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipHeaderInvalidDetail, path);
    }

    [Event(EvtWarmupClipSampleMismatch,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The warmup clip is not in the expected audio format, so the model warms up on silence")]
    public void WarmupClipSampleMismatch()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipSampleMismatch);
    }

    [Event(EvtWarmupClipSampleMismatchDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "audio_format={0} | num_channels={1} | sample_rate={2} | bits_per_sample={3}")]
    public void WarmupClipSampleMismatchDetail(int audio_format, int num_channels, int sample_rate, int bits_per_sample)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipSampleMismatchDetail, audio_format, num_channels, sample_rate, bits_per_sample);
    }

    [Event(EvtWarmupClipLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The warmup clip could not be loaded, the model warms up on silence\nex_type={0} | ex_message={1}")]
    public void WarmupClipLoadFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupClipLoadFailed, ex_type, ex_message);
    }

    // ── Warmup pipeline ──────────────────────────────────────────────────
    //
    // WarmupStart always ends with WarmupComplete, WarmupCancelled or
    // WarmupFailed: EnsurePrimed emits one on every exit, thrown ones included.
    // The pair travels in the log and shares `work_id`, which is not the id
    // of the model load the warmup triggers: that load is its own work.

    [Event(EvtWarmupStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Warmup start")]
    public void WarmupStart(string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupStart, work_id);
    }

    [Event(EvtWarmupComplete,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Warmup complete")]
    public void WarmupComplete(string work_id, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupComplete, work_id, elapsed_ms);
    }

    // A Stop or Dispose that lands during the prime; the next take primes again.
    [Event(EvtWarmupCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Warmup cancelled")]
    public void WarmupCancelled(string work_id, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupCancelled, work_id, elapsed_ms);
    }

    // reason (closed vocabulary): model_load_failed, whose cause the
    // ModelUnavailable incident already carries (ex_type empty); test_failed,
    // the test inference failed on both attempts, whose causes the two
    // WarmupTestFailed lines carry (ex_type empty); or prime_exception, an
    // exception outside the test inference. None of them throws: the gate
    // yields false and the take ends as a failure.
    [Event(EvtWarmupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The transcription model could not be warmed up\nwork_id={0} | reason={1} | ex_type={2} | ex_message={3} | elapsed_ms={4}")]
    public void WarmupFailed(string work_id, string reason, string ex_type, string ex_message, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupFailed, work_id, reason, ex_type, ex_message, elapsed_ms);
    }

    // The test inference that opens every take failed: the backend threw, or
    // returned a native error code. attempt is 1 for the first test and 2 for
    // the one after the engine unloaded and reloaded the model; the first
    // failure is retried silently, a second ends the take. reason (closed
    // vocabulary): exception (ex_type and ex_message carry it) | bad_result
    // (result_code carries the native code, ex_message the backend's reason).
    // The four memory fields are read at the moment of the failure, in
    // megabytes: available and total physical memory, available commit and
    // commit limit. All zero means the query failed.
    [Event(EvtWarmupTestFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The warm-up inference failed\nwork_id={0} | attempt={1} | reason={2} | ex_type={3} | ex_message={4} | result_code={5} | avail_phys_mb={6} | total_phys_mb={7} | avail_commit_mb={8} | commit_limit_mb={9} | elapsed_ms={10}")]
    public void WarmupTestFailed(
        string work_id, int attempt, string reason, string ex_type, string ex_message, int result_code,
        long avail_phys_mb, long total_phys_mb, long avail_commit_mb, long commit_limit_mb, long elapsed_ms)
    {
        if (IsEnabled())
            WriteEvent(EvtWarmupTestFailed, work_id, attempt, reason, ex_type, ex_message, result_code,
                avail_phys_mb, total_phys_mb, avail_commit_mb, commit_limit_mb, elapsed_ms);
    }

    // Every take: prime_ms is the model load (zero work when the model was
    // already resident) plus the test inference,
    // gate_wait_ms how long the first real backend call waited for it, and
    // gate_phase (closed vocabulary) where that wait sat, which sets its meaning:
    //   at_stop          (monolithic) — post-Stop latency the user perceived;
    //                     ≈ 0 means the recording hid the whole cold cost.
    //   during_recording (segmented)  — the consumer waited while capture ran;
    //                     a large value is hidden cost, not user-facing latency.
    //   during_file_decode, during_recovery — the batch or the launch recovery
    //                     waited for the model; nobody is recording meanwhile.
    [Event(EvtPrimeOverlap,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "prime_ms={0} | gate_wait_ms={1} | gate_phase={2}")]
    public void PrimeOverlap(long prime_ms, long gate_wait_ms, string gate_phase)
    {
        if (IsEnabled()) WriteEvent(EvtPrimeOverlap, prime_ms, gate_wait_ms, gate_phase);
    }

    // ── Model lifecycle ─────────────────────────────────────────────────

    // The load of the model is lasting work: ModelLoading opens it and
    // ModelLoaded or ModelUnavailable closes it, sharing `work_id`. While the
    // "no transcription" incident is open every take retries the load, and
    // only the first failure is told: those retries start with
    // ModelLoadRetrying instead, which stays out of the log like their
    // ModelLoadRetryFailed end. The success that ends the incident is
    // ModelRecovered, which repeats the id of the ModelUnavailable it closes.
    [Event(EvtModelLoading,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Loading model")]
    public void ModelLoading(string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoading, work_id);
    }

    [Event(EvtModelLoadRetrying,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "work_id={0}")]
    public void ModelLoadRetrying(string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadRetrying, work_id);
    }

    [Event(EvtModelInitFromFile,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ctx={0}")]
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
           Tags = ObservationTags.Milestone,
           Message = "The transcription model could not be loaded, so dictation cannot be transcribed\nwork_id={0} | reason={1} | ex_type={2} | ex_message={3}")]
    public void ModelUnavailable(string work_id, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtModelUnavailable, work_id, reason, ex_type, ex_message);
    }

    // A later attempt that fails while the incident is open.
    [Event(EvtModelLoadRetryFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "work_id={0} | reason={1} | ex_type={2} | ex_message={3}")]
    public void ModelLoadRetryFailed(string work_id, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadRetryFailed, work_id, reason, ex_type, ex_message);
    }

    // work_id is the one of the load whose failure opened the incident, so the
    // file pairs the ModelUnavailable that opened it with this line that closes
    // it. load_ms and device describe the load that succeeded. device is the
    // backend's accelerator word ("CPU", "Vulkan0", "unknown").
    [Event(EvtModelRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Transcription model availability recovered")]
    public void ModelRecovered(string work_id, long load_ms, string device)
    {
        if (IsEnabled()) WriteEvent(EvtModelRecovered, work_id, load_ms, device);
    }

    [Event(EvtModelLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Model loaded")]
    public void ModelLoaded(string work_id, long load_ms, string device)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoaded, work_id, load_ms, device);
    }

    [Event(EvtModelOnDemandLoad,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Whisper model loads on demand")]
    public void ModelOnDemandLoad()
    {
        if (IsEnabled()) WriteEvent(EvtModelOnDemandLoad);
    }

    [Event(EvtModelIdleUnloadSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "state={0}")]
    public void ModelIdleUnloadSkipped(string state)
    {
        if (IsEnabled()) WriteEvent(EvtModelIdleUnloadSkipped, state);
    }

    // The idle-unload timer runs on the thread pool, where an escaping
    // exception ends the process; the model then stays loaded until the next
    // take re-arms the timer. Transcription goes on, so this is a degradation
    // (a Warning), not a stop.
    [Event(EvtModelIdleUnloadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The idle model unload failed, the model stays loaded\nex_type={0} | ex_message={1}")]
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
           Message = "idle_s={0}")]
    public void ModelUnloaded(int idle_s)
    {
        if (IsEnabled()) WriteEvent(EvtModelUnloaded, idle_s);
    }

    [Event(EvtModelIdleTimerSet,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "idle_s={0}")]
    public void ModelIdleTimerSet(int idle_s)
    {
        if (IsEnabled()) WriteEvent(EvtModelIdleTimerSet, idle_s);
    }

    // The variable named a file that is not an existing absolute path. The
    // Warning names what was obtained, the file the settings resolved to; the
    // value of the variable can carry a user path and stays in the Verbose
    // detail.
    [Event(EvtModelPathEnvIgnored,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The model path variable is not an existing absolute path, so the default model is used\nfallback_file={0}")]
    public void ModelPathEnvIgnored(string fallback_file)
    {
        if (IsEnabled()) WriteEvent(EvtModelPathEnvIgnored, fallback_file);
    }

    [Event(EvtModelPathEnvIgnoredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "env_path={0} | fallback={1}")]
    public void ModelPathEnvIgnoredDetail(string env_path, string fallback)
    {
        if (IsEnabled()) WriteEvent(EvtModelPathEnvIgnoredDetail, env_path, fallback);
    }

    // The Warning names what was asked and what was obtained, both file names
    // in the models directory.
    [Event(EvtModelFallback,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The configured model file is missing, so an installed model is used instead\nconfigured={0} | installed={1}")]
    public void ModelFallback(string configured, string installed)
    {
        if (IsEnabled()) WriteEvent(EvtModelFallback, configured, installed);
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
           Message = "summary={0}")]
    public void WhisperInitParamsParsed(string summary)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperInitParamsParsed, summary);
    }

    [Event(EvtWhisperModelLoadParsed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "summary={0}")]
    public void WhisperModelLoadParsed(string summary)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperModelLoadParsed, summary);
    }

    [Event(EvtWhisperBackendInitParsed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "summary={0}")]
    public void WhisperBackendInitParsed(string summary)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperBackendInitParsed, summary);
    }

    [Event(EvtWhisperInitStateParsed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "summary={0}")]
    public void WhisperInitStateParsed(string summary)
    {
        if (IsEnabled()) WriteEvent(EvtWhisperInitStateParsed, summary);
    }
}

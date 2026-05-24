using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

// Whisp module provider. Couvre le moteur de transcription
// (TranscriptionEngine), le cycle de vie du modèle Whisper natif (chargement
// paresseux, idle unload), le warmup boot, la transcription elle-même
// (params, prompt, segments, complétion), le clipboard, le paste, la
// redirection des logs whisper.cpp, et les heartbeats structurés de
// fin de pipeline (LatencyRecorded, CorpusAsrRecorded,
// CorpusRewriteRecorded). La persistance settings du module passe par
// les quatre events Settings* transitoires (entorse documentée — voir
// DeckleAudioSource).
//
// Choix de design — entorses notables :
//
//   1. Les anciens narratifs (`_log.Narrative`) sont abandonnés.
//      EventLevel n'a pas de niveau "Narrative" — ces lignes étaient
//      des reformulations de prose qui ne portaient pas d'info
//      nouvelle par-dessus les jalons Info et les verbose structurés
//      qui les précèdent.
//
//   2. LatencyRecorded et CorpusRecorded sont les events JSONL
//      canoniques (filtrés par TelemetryListenerBootstrap). Les
//      trois lignes humaines [DONE] timings / llm_metrics / outputs
//      restent émises en parallèle sous PipelineTimings,
//      PipelineLlmMetrics et PipelineOutputs : c'est la lecture
//      pour humains côté LogWindow. JsonlEventListener latency.jsonl
//      ne récupère que LatencyRecorded (24 champs aplatis), pas les
//      trois lignes humaines.
//
//   3. Les UserFeedback sont émises via le canal canonique
//      UserFeedbackEmitted(severity, title, body, role) — sévérité
//      0/1/2 = Info/Warning/Error, rôle 0/1 = Replacement/Overlay.
//      Le mapping vit côté HudFeedbackEventListener.
[EventSource(Name = "Deckle.Whisp")]
public sealed class DeckleWhispSource : DeckleEventSource
{
    public static readonly DeckleWhispSource Log = new();

    private DeckleWhispSource() { }

    // ── EventIds — séquentiels à partir de 1, jamais réutilisés ─────────
    public const int EvtWarmupClipMissing                = 1;
    public const int EvtWarmupClipHeaderInvalid          = 2;
    public const int EvtWarmupClipSampleMismatch         = 3;
    public const int EvtWarmupClipLoadFailed             = 4;
    public const int EvtWarmupStart                      = 5;
    public const int EvtWarmupCancelledBeforeModel       = 6;
    public const int EvtWarmupAbortedModelLoad           = 7;
    public const int EvtWarmupCancelledBeforeTranscribe  = 8;
    public const int EvtWarmupCancelledDuringTranscribe  = 9;
    public const int EvtWarmupComplete                   = 10;
    public const int EvtWarmupCompleteDetail             = 11;
    public const int EvtWarmupFailed                     = 12;
    public const int EvtWarmupFlagModelKO                = 13;
    public const int EvtWarmupFlagOllamaKO               = 14;
    public const int EvtWarmupFlagOllamaRecovered        = 15;
    public const int EvtWarmupFlagMicKO                  = 16;
    public const int EvtModelLoading                     = 17;
    public const int EvtModelLoadStart                   = 18;
    public const int EvtModelLoadAborted                 = 19;
    public const int EvtModelInitFromFile                = 20;
    public const int EvtModelLoadFailed                  = 21;
    public const int EvtModelLoaded                      = 22;
    public const int EvtModelLoadComplete                = 23;
    public const int EvtModelOnDemandLoad                = 24;
    public const int EvtModelIdleUnloadSkipped           = 25;
    public const int EvtModelUnloadedJalon               = 26;
    public const int EvtModelUnloaded                    = 27;
    public const int EvtModelIdleTimerSet                = 28;
    public const int EvtModelPathEnvIgnored              = 29;
    public const int EvtWhisperLogVerbose                = 30;
    public const int EvtWhisperLogWarning                = 31;
    public const int EvtWhisperLogError                  = 32;
    public const int EvtWhisperLogSetUnavailable         = 33;
    public const int EvtVadParsed                        = 34;
    public const int EvtHotkeyToggleIgnored              = 35;
    public const int EvtHotkeyStartingCASLost            = 36;
    public const int EvtRecordingProbeFailed             = 37;
    public const int EvtRecordingMicError                = 38;
    public const int EvtRecordingLowAudio                = 39;
    public const int EvtAutoCalibrated                   = 40;
    public const int EvtPipelineCrashed                  = 41;
    public const int EvtTranscribeStarted                = 42;
    public const int EvtTranscribeStartDetail            = 43;
    public const int EvtTranscribeParams                 = 44;
    public const int EvtTranscribePrompt                 = 45;
    public const int EvtTranscribeEmpty                  = 46;
    public const int EvtTranscribeFailed                 = 47;
    public const int EvtTranscribeCompleted              = 48;
    public const int EvtTranscribeCompleteDetail         = 49;
    public const int EvtTranscribeRepetitionLoop         = 50;
    public const int EvtTranscribeSkipped                = 51;
    public const int EvtSegmentEmitted                   = 52;
    public const int EvtSegmentCallbackThrew             = 53;
    public const int EvtClipboardGlobalAlloc             = 54;
    public const int EvtClipboardAllocFailed             = 55;
    public const int EvtClipboardOpen                    = 56;
    public const int EvtClipboardOpenFailed              = 57;
    public const int EvtClipboardSetDataFailed           = 58;
    public const int EvtClipboardVerifyMissing           = 59;
    public const int EvtClipboardVerifyMismatch          = 60;
    public const int EvtClipboardCopied                  = 61;
    public const int EvtClipboardCopyComplete            = 62;
    public const int EvtPasteHidSync                     = 63;
    public const int EvtPasteForeground                  = 64;
    public const int EvtPasteSkippedNoForeground         = 65;
    public const int EvtPasteSkippedSelfTarget           = 66;
    public const int EvtPasteUiaDiag                     = 67;
    public const int EvtPasteSkippedNotTextField         = 68;
    public const int EvtPasteSendInputPartial            = 69;
    public const int EvtPasteSucceeded                   = 70;
    public const int EvtPasteSent                        = 71;
    public const int EvtPipelineCompleted                = 72;
    public const int EvtPipelineTimings                  = 73;
    public const int EvtPipelineLlmMetrics               = 74;
    public const int EvtPipelineOutputs                  = 75;
    public const int EvtLatencyRecorded                  = 76;
    public const int EvtCorpusRecorded                   = 77;
    public const int EvtUserFeedbackEmitted              = 78;
    public const int EvtManualProfileNotFound            = 79;
    public const int EvtDisposeWorkerJoinTimeout         = 80;
    public const int EvtDisposeWorkerJoined              = 81;
    public const int EvtDisposeStart                     = 82;
    public const int EvtSettingsLoaded                   = 83;
    public const int EvtSettingsLoadComplete             = 84;
    public const int EvtSettingsLoadWarning              = 85;
    public const int EvtSettingsLoadError                = 86;
    public const int EvtSettingChanged                   = 87;
    public const int EvtPageInitStart                    = 88;
    public const int EvtPageInitComplete                 = 89;
    public const int EvtPageBuggedSliderSet              = 90;
    public const int EvtPageInitFailed                   = 91;
    public const int EvtPageLoadedStart                  = 92;
    public const int EvtPageReady                        = 93;
    public const int EvtPageLoadedComplete               = 94;
    public const int EvtPageLoadedFailed                 = 95;
    public const int EvtPageModelScanFailed              = 96;
    public const int EvtPageDiscardRestartChanges        = 97;
    public const int EvtPageResetAll                     = 98;
    public const int EvtPageStackTrace                   = 99;
    public const int EvtWhispSettingsPrefixed            = 100;
    public const int EvtWhisperInitParamsParsed          = 101;
    public const int EvtWhisperModelLoadParsed           = 102;
    public const int EvtWhisperBackendInitParsed         = 103;
    public const int EvtWhisperInitStateParsed           = 104;
    public const int EvtCorpusAsrRecorded                = 105;
    public const int EvtCorpusRewriteRecorded            = 106;

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

    [Event(EvtWarmupCancelledBeforeModel,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup cancelled before model load | total_ms={0}")]
    public void WarmupCancelledBeforeModel(long total_ms)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupCancelledBeforeModel, total_ms);
    }

    [Event(EvtWarmupAbortedModelLoad,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup aborted | reason=model_load_failed | total_ms={0} | mic_ok={1} | model_ok=false | ollama_ok=skipped")]
    public void WarmupAbortedModelLoad(long total_ms, bool mic_ok)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupAbortedModelLoad, total_ms, mic_ok);
    }

    [Event(EvtWarmupCancelledBeforeTranscribe,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup cancelled before transcribe | total_ms={0}")]
    public void WarmupCancelledBeforeTranscribe(long total_ms)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupCancelledBeforeTranscribe, total_ms);
    }

    [Event(EvtWarmupCancelledDuringTranscribe,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup cancelled during transcribe | total_ms={0}")]
    public void WarmupCancelledDuringTranscribe(long total_ms)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupCancelledDuringTranscribe, total_ms);
    }

    [Event(EvtWarmupComplete,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup complete")]
    public void WarmupComplete()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupComplete);
    }

    [Event(EvtWarmupCompleteDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup complete | total_ms={0} | mic_ok={1} | model_ok={2} | ollama_ok={3}")]
    public void WarmupCompleteDetail(long total_ms, bool mic_ok, bool model_ok, bool ollama_ok)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupCompleteDetail, total_ms, mic_ok, model_ok, ollama_ok);
    }

    [Event(EvtWarmupFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Warmup failed | error={0}: {1}")]
    public void WarmupFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWarmupFailed, ex_type, ex_message);
    }

    [Event(EvtWarmupFlagModelKO,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup flag | model_ok=false")]
    public void WarmupFlagModelKO()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupFlagModelKO);
    }

    [Event(EvtWarmupFlagOllamaKO,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup flag | ollama_ok=false (live re-probe also failed)")]
    public void WarmupFlagOllamaKO()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupFlagOllamaKO);
    }

    [Event(EvtWarmupFlagOllamaRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup flag | ollama_ok=false but live re-probe ok — proceeding without warning")]
    public void WarmupFlagOllamaRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupFlagOllamaRecovered);
    }

    [Event(EvtWarmupFlagMicKO,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "warmup flag | mic_ok=false (live probe below)")]
    public void WarmupFlagMicKO()
    {
        if (IsEnabled()) WriteEvent(EvtWarmupFlagMicKO);
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

    [Event(EvtVadParsed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "vad: {0}")]
    public void VadParsed(string summary)
    {
        if (IsEnabled()) WriteEvent(EvtVadParsed, summary);
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

    // ── Hotkey / start gating ───────────────────────────────────────────

    [Event(EvtHotkeyToggleIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "toggle ignored | state={0}")]
    public void HotkeyToggleIgnored(string state)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyToggleIgnored, state);
    }

    [Event(EvtHotkeyStartingCASLost,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "starting → recording CAS lost (likely Dispose)")]
    public void HotkeyStartingCASLost()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStartingCASLost);
    }

    [Event(EvtRecordingProbeFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "probe MMSYSERR={0} — {1}")]
    public void RecordingProbeFailed(uint mmsys_err, string title)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingProbeFailed, mmsys_err, title);
    }

    [Event(EvtRecordingMicError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture mic error MMSYSERR={0} — {1}")]
    public void RecordingMicError(uint mmsys_err, string title)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingMicError, mmsys_err, title);
    }

    [Event(EvtRecordingLowAudio,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "low audio overlay surfaced")]
    public void RecordingLowAudio()
    {
        if (IsEnabled()) WriteEvent(EvtRecordingLowAudio);
    }

    [Event(EvtAutoCalibrated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Auto-calibrated level window: Min={0:F0} Max={1:F0} dBFS (median over {2} sessions, p25-5dB / p90+5dB margins)")]
    public void AutoCalibrated(double new_min_dbfs, double new_max_dbfs, int session_count)
    {
        if (IsEnabled()) WriteEvent(EvtAutoCalibrated, new_min_dbfs, new_max_dbfs, session_count);
    }

    [Event(EvtPipelineCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "pipeline crashed: {0}: {1}")]
    public void PipelineCrashed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineCrashed, ex_type, ex_message);
    }

    // ── Transcribe ──────────────────────────────────────────────────────

    [Event(EvtTranscribeStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcribing")]
    public void TranscribeStarted()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeStarted);
    }

    [Event(EvtTranscribeStartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "start | audio_sec={0:F1} | samples={1} | strategy={2}")]
    public void TranscribeStartDetail(double audio_sec, int samples, string strategy)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeStartDetail, audio_sec, samples, strategy);
    }

    [Event(EvtTranscribeParams,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "params | {0}")]
    public void TranscribeParams(string params_text)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeParams, params_text);
    }

    [Event(EvtTranscribePrompt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "prompt | len={0} | carry={1} | text=\"{2}\"")]
    public void TranscribePrompt(int prompt_len, bool carry, string preview)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribePrompt, prompt_len, carry, preview);
    }

    [Event(EvtTranscribeEmpty,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "empty audio buffer, nothing to transcribe")]
    public void TranscribeEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeEmpty);
    }

    [Event(EvtTranscribeFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "whisper_full failed | result={0}")]
    public void TranscribeFailed(int result)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeFailed, result);
    }

    [Event(EvtTranscribeCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcription complete ({0} seg)")]
    public void TranscribeCompleted(int n_seg)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeCompleted, n_seg);
    }

    [Event(EvtTranscribeCompleteDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "complete | whisper_ms={0} | n_seg={1} | chars={2}")]
    public void TranscribeCompleteDetail(long whisper_ms, int n_seg, int chars)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeCompleteDetail, whisper_ms, n_seg, chars);
    }

    [Event(EvtTranscribeRepetitionLoop,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "repetition loop detected — {0} identical segments ('{1}'); requesting whisper to abort")]
    public void TranscribeRepetitionLoop(int streak, string preview)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeRepetitionLoop, streak, preview);
    }

    [Event(EvtTranscribeSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "skip transcribe | state={0}")]
    public void TranscribeSkipped(string state)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeSkipped, state);
    }

    // ── Segment callback ────────────────────────────────────────────────

    [Event(EvtSegmentEmitted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}")]
    public void SegmentEmitted(string segment_line)
    {
        if (IsEnabled()) WriteEvent(EvtSegmentEmitted, segment_line);
    }

    [Event(EvtSegmentCallbackThrew,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}: {1}")]
    public void SegmentCallbackThrew(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackThrew, ex_type, ex_message);
    }

    // ── Clipboard ───────────────────────────────────────────────────────

    [Event(EvtClipboardGlobalAlloc,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "GlobalAlloc | bytes={0} | hMem={1}")]
    public void ClipboardGlobalAlloc(int bytes, long h_mem)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardGlobalAlloc, bytes, h_mem);
    }

    [Event(EvtClipboardAllocFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "GlobalAlloc failed | bytes={0}")]
    public void ClipboardAllocFailed(int bytes)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardAllocFailed, bytes);
    }

    [Event(EvtClipboardOpen,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "OpenClipboard | ok={0}")]
    public void ClipboardOpen(bool ok)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardOpen, ok);
    }

    [Event(EvtClipboardOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "OpenClipboard failed")]
    public void ClipboardOpenFailed()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardOpenFailed);
    }

    [Event(EvtClipboardSetDataFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "SetClipboardData failed | handle=0")]
    public void ClipboardSetDataFailed()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardSetDataFailed);
    }

    [Event(EvtClipboardVerifyMissing,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "verify failed | reason=no_unicode_data")]
    public void ClipboardVerifyMissing()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMissing);
    }

    [Event(EvtClipboardVerifyMismatch,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "verify failed | expected_chars={0} | actual_chars={1}")]
    public void ClipboardVerifyMismatch(int expected_chars, int actual_chars)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardVerifyMismatch, expected_chars, actual_chars);
    }

    [Event(EvtClipboardCopied,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Copied to clipboard")]
    public void ClipboardCopied()
    {
        if (IsEnabled()) WriteEvent(EvtClipboardCopied);
    }

    [Event(EvtClipboardCopyComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "copy complete | chars={0} | bytes={1}")]
    public void ClipboardCopyComplete(int chars, int bytes)
    {
        if (IsEnabled()) WriteEvent(EvtClipboardCopyComplete, chars, bytes);
    }

    // ── Paste ───────────────────────────────────────────────────────────

    [Event(EvtPasteHidSync,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "HUD hidden (HideSync) — ready to paste")]
    public void PasteHidSync()
    {
        if (IsEnabled()) WriteEvent(EvtPasteHidSync);
    }

    [Event(EvtPasteForeground,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "foreground at paste: {0}")]
    public void PasteForeground(string foreground_descriptor)
    {
        if (IsEnabled()) WriteEvent(EvtPasteForeground, foreground_descriptor);
    }

    [Event(EvtPasteSkippedNoForeground,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "skipped: no foreground window. Clipboard holds the text — Ctrl+V where you want it.")]
    public void PasteSkippedNoForeground()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedNoForeground);
    }

    [Event(EvtPasteSkippedSelfTarget,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "skipped: foreground is Deckle itself. Clipboard holds the text — Ctrl+V in the right window.")]
    public void PasteSkippedSelfTarget()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedSelfTarget);
    }

    [Event(EvtPasteUiaDiag,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "UIA: {0}")]
    public void PasteUiaDiag(string uia_diag)
    {
        if (IsEnabled()) WriteEvent(EvtPasteUiaDiag, uia_diag);
    }

    [Event(EvtPasteSkippedNotTextField,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "skipped: focused element is not a text field. Clipboard holds the text — Ctrl+V where you want it.")]
    public void PasteSkippedNotTextField()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSkippedNotTextField);
    }

    [Event(EvtPasteSendInputPartial,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "partial: SendInput injected {0}/{1} events. Clipboard holds the text — Ctrl+V manually.")]
    public void PasteSendInputPartial(int sent, int total)
    {
        if (IsEnabled()) WriteEvent(EvtPasteSendInputPartial, sent, total);
    }

    [Event(EvtPasteSucceeded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Pasted")]
    public void PasteSucceeded()
    {
        if (IsEnabled()) WriteEvent(EvtPasteSucceeded);
    }

    [Event(EvtPasteSent,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Ctrl+V sent to {0}")]
    public void PasteSent(string foreground_descriptor)
    {
        if (IsEnabled()) WriteEvent(EvtPasteSent, foreground_descriptor);
    }

    // ── Pipeline completion ─────────────────────────────────────────────

    [Event(EvtPipelineCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Done ({0})")]
    public void PipelineCompleted(string outcome)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineCompleted, outcome);
    }

    [Event(EvtPipelineTimings,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "timings | audio_sec={0:F1} | model_load_ms={1} | hotkey_to_capture_ms={2} | record_drain_ms={3} | stop_to_pipeline_ms={4} | whisper_init_ms={5} | vad_ms={6} | vad_inference_ms={7} | whisper_ms={8} | llm_ms={9} | clipboard_ms={10} | paste_ms={11}")]
    public void PipelineTimings(double audio_sec, long model_load_ms, long hotkey_to_capture_ms, long record_drain_ms, long stop_to_pipeline_ms, long whisper_init_ms, long vad_ms, long vad_inference_ms, long whisper_ms, long llm_ms, long clipboard_ms, long paste_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtPipelineTimings, audio_sec, model_load_ms, hotkey_to_capture_ms, record_drain_ms, stop_to_pipeline_ms, whisper_init_ms, vad_ms, vad_inference_ms, whisper_ms, llm_ms, clipboard_ms, paste_ms);
    }

    [Event(EvtPipelineLlmMetrics,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "llm_metrics | ollama_load_ms={0} | prompt_eval_ms={1} | eval_ms={2} | prompt_tokens={3} | eval_tokens={4}")]
    public void PipelineLlmMetrics(long ollama_load_ms, long prompt_eval_ms, long eval_ms, int prompt_tokens, int eval_tokens)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineLlmMetrics, ollama_load_ms, prompt_eval_ms, eval_ms, prompt_tokens, eval_tokens);
    }

    [Event(EvtPipelineOutputs,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "outputs | n_seg={0} | chars={1} | words={2} | strategy={3} | profile={4} | outcome={5}")]
    public void PipelineOutputs(int n_seg, int chars, int words, string strategy, string profile, string outcome)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineOutputs, n_seg, chars, words, strategy, profile, outcome);
    }

    // ── Heartbeats structurés — JSONL canoniques ────────────────────────
    //
    // LatencyRecorded, CorpusAsrRecorded et CorpusRewriteRecorded sont
    // les events que JsonlEventListener (et RoutedJsonlEventListener pour
    // les deux corpus) filtrent pour écrire latency.jsonl et les
    // corpus.jsonl bucketés. Le format Message est un récap mono-ligne
    // pour LogWindow ; le payload complet est sérialisé par
    // EtwSelfDescribingEventFormat avec les noms snake_case devenant
    // les clés JSON.
    //
    // CorpusAsrRecorded capture la sortie ASR (Whisper, plus tard
    // Voxtral). Routée vers corpus/<bucket>/<tier>/corpus.jsonl
    // (bucket=raw en mode mot-pour-mot, bucket=voxtral-<instruction>
    // quand le mode instruction-nommée Voxtral sera branché). Les
    // cinq tiers de longueur — very-short / short / medium / long /
    // very-long — découpent le dataset par charge ASR pour l'analyse.
    //
    // CorpusRewriteRecorded capture la sortie réécriture LLM. Routée
    // vers corpus/rewrite-<name>-<id>/corpus.jsonl (plat — pas de tier
    // sur le rewrite, voir ADR-0011). Le rewrite_profile_id sert de
    // jointure avec le profil ; le prompt_template_hash invalide les
    // analyses si le template change sans rename d'ID.
    //
    // Quand un rewrite tourne, les deux events partent avec le même
    // transcription_id — c'est la clé qui joint les lignes au WAV
    // (audio/<transcription_id>.wav).
    //
    // L'ancien event CorpusRecorded est conservé temporairement le temps
    // de la transition pour ne pas casser un éventuel consommateur. Sera
    // retiré en fin de chantier — voir ADR-0011 section Conséquences.

    [Event(EvtLatencyRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "audio={0:F1}s hotkey={2}ms vad={6}ms whisper={8}ms llm={9}ms outcome={23}")]
    public void LatencyRecorded(
        double audio_sec,
        long   model_load_ms,
        long   hotkey_to_capture_ms,
        long   record_drain_ms,
        long   stop_to_pipeline_ms,
        long   whisper_init_ms,
        long   vad_ms,
        long   vad_inference_ms,
        long   whisper_ms,
        long   llm_ms,
        long   ollama_load_ms,
        long   llm_prompt_eval_ms,
        long   llm_eval_ms,
        int    llm_prompt_tokens,
        int    llm_eval_tokens,
        long   clipboard_ms,
        long   paste_ms,
        string strategy,
        int    n_segments,
        int    text_chars,
        int    text_words,
        string profile,
        bool   pasted,
        string outcome)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtLatencyRecorded,
            audio_sec, model_load_ms, hotkey_to_capture_ms, record_drain_ms,
            stop_to_pipeline_ms, whisper_init_ms, vad_ms, vad_inference_ms,
            whisper_ms, llm_ms, ollama_load_ms, llm_prompt_eval_ms, llm_eval_ms,
            llm_prompt_tokens, llm_eval_tokens, clipboard_ms, paste_ms,
            strategy, n_segments, text_chars, text_words, profile, pasted, outcome);
    }

    [System.Obsolete(
        "Remplacé par CorpusAsrRecorded + CorpusRewriteRecorded (ADR-0011). " +
        "Conservé temporairement pour cohabiter avec le listener legacy " +
        "corpus.jsonl pendant la transition — sera retiré en fin de chantier.")]
    [Event(EvtCorpusRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "profile={2} words={9} wps={11:F1}")]
    public void CorpusRecorded(
        string profile,
        string profile_id,
        string slug,
        double duration_seconds,
        string model,
        string language,
        long   elapsed_ms,
        string initial_prompt,
        string raw_text,
        int    raw_words,
        int    raw_chars,
        double words_per_second,
        string audio_file)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtCorpusRecorded,
            profile, profile_id, slug, duration_seconds,
            model, language, elapsed_ms, initial_prompt,
            raw_text, raw_words, raw_chars, words_per_second, audio_file);
    }

    [Event(EvtCorpusAsrRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "asr | bucket={2} | tier={3} | words={9} | wps={12:F1}")]
    public void CorpusAsrRecorded(
        string transcription_id,
        string audio_file,
        string bucket,
        string tier,
        string backend,
        string model,
        string language,
        string prompt_or_instruction,
        string text,
        int    text_words,
        int    text_chars,
        double duration_seconds,
        double words_per_second,
        long   elapsed_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtCorpusAsrRecorded,
            transcription_id, audio_file, bucket, tier,
            backend, model, language, prompt_or_instruction,
            text, text_words, text_chars, duration_seconds,
            words_per_second, elapsed_ms);
    }

    [Event(EvtCorpusRewriteRecorded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "rewrite | bucket={2} | profile={4} | words={9} | elapsed_ms={11}")]
    public void CorpusRewriteRecorded(
        string transcription_id,
        string audio_file,
        string bucket,
        string rewrite_profile_id,
        string rewrite_profile_name,
        string ollama_endpoint,
        string ollama_model,
        string prompt_template_hash,
        string text,
        int    text_words,
        int    text_chars,
        long   elapsed_ms)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(EvtCorpusRewriteRecorded,
            transcription_id, audio_file, bucket,
            rewrite_profile_id, rewrite_profile_name,
            ollama_endpoint, ollama_model, prompt_template_hash,
            text, text_words, text_chars, elapsed_ms);
    }

    // ── UserFeedback ────────────────────────────────────────────────────
    //
    // Canal canonique pour les notifications utilisateur (HUD Replacement
    // / Overlay). Sévérité 0/1/2 = Info/Warning/Error, rôle 0/1 =
    // Replacement/Overlay. Filtré par HudFeedbackEventListener.

    [Event(EvtUserFeedbackEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{1}: {2}")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }

    // ── Llm-side observation depuis TranscriptionEngine ────────────────────────

    [Event(EvtManualProfileNotFound,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "manual profile '{0}' not found in Profiles — transcript pasted without rewriting. Pick an existing profile on the Rewriting page.")]
    public void ManualProfileNotFound(string profile_name)
    {
        if (IsEnabled()) WriteEvent(EvtManualProfileNotFound, profile_name);
    }

    // ── Dispose ─────────────────────────────────────────────────────────

    [Event(EvtDisposeStart,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "dispose | waiting on worker | prev_state={0} | timeout_ms={1}")]
    public void DisposeStart(string prev_state, int timeout_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDisposeStart, prev_state, timeout_ms);
    }

    [Event(EvtDisposeWorkerJoinTimeout,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "dispose timeout | join_ms={0} — worker still alive, leaking thread (process exiting)")]
    public void DisposeWorkerJoinTimeout(long join_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDisposeWorkerJoinTimeout, join_ms);
    }

    [Event(EvtDisposeWorkerJoined,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "dispose | worker joined | join_ms={0}")]
    public void DisposeWorkerJoined(long join_ms)
    {
        if (IsEnabled()) WriteEvent(EvtDisposeWorkerJoined, join_ms);
    }

    // ── Settings persistence (transitoire — voir DeckleAudioSource) ─────

    [Event(EvtSettingsLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoaded(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoaded, message);
    }

    [Event(EvtSettingsLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadComplete(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadComplete, message);
    }

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    // ── TranscriptionSettings persistence (préfixés [whisp] dans le legacy) ─────
    // Entorse identique à celle de DeckleAudioSource pour les delegates
    // JsonSettingsStore — pas connu au site d'appel quelle opération
    // exacte est en cours. Préservé séparé de SettingsLoaded* pour
    // permettre au LegacyLogWindowSink de garder le préfixe [whisp]
    // dans le message tant que le moteur legacy LogWindow vit encore.

    [Event(EvtWhispSettingsPrefixed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void WhispSettingsPrefixed(string message)
    {
        if (IsEnabled()) WriteEvent(EvtWhispSettingsPrefixed, message);
    }

    // ── ViewModel + WhisperPage UI side ─────────────────────────────────

    [Event(EvtSettingChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0} ← {1}")]
    public void SettingChanged(string property, string value)
    {
        if (IsEnabled()) WriteEvent(EvtSettingChanged, property, value);
    }

    [Event(EvtPageInitStart,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ctor start")]
    public void PageInitStart()
    {
        if (IsEnabled()) WriteEvent(EvtPageInitStart);
    }

    [Event(EvtPageInitComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "init component complete")]
    public void PageInitComplete()
    {
        if (IsEnabled()) WriteEvent(EvtPageInitComplete);
    }

    [Event(EvtPageBuggedSliderSet,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bugged slider min/max set in code-behind")]
    public void PageBuggedSliderSet()
    {
        if (IsEnabled()) WriteEvent(EvtPageBuggedSliderSet);
    }

    [Event(EvtPageInitFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "init component threw | error={0}: {1}")]
    public void PageInitFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageInitFailed, ex_type, ex_message);
    }

    [Event(EvtPageLoadedStart,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "loaded fired")]
    public void PageLoadedStart()
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedStart);
    }

    [Event(EvtPageReady,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Whisper page ready")]
    public void PageReady()
    {
        if (IsEnabled()) WriteEvent(EvtPageReady);
    }

    [Event(EvtPageLoadedComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "loaded complete | state=page-ready")]
    public void PageLoadedComplete()
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedComplete);
    }

    [Event(EvtPageLoadedFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "loaded threw | error={0}: {1}")]
    public void PageLoadedFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageLoadedFailed, ex_type, ex_message);
    }

    [Event(EvtPageModelScanFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "model scan failed: {0}")]
    public void PageModelScanFailed(string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPageModelScanFailed, ex_message);
    }

    [Event(EvtPageDiscardRestartChanges,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Discard restart-requiring changes")]
    public void PageDiscardRestartChanges()
    {
        if (IsEnabled()) WriteEvent(EvtPageDiscardRestartChanges);
    }

    [Event(EvtPageResetAll,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Reset all Whisper settings to defaults")]
    public void PageResetAll()
    {
        if (IsEnabled()) WriteEvent(EvtPageResetAll);
    }

    [Event(EvtPageStackTrace,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void PageStackTrace(string stack_trace)
    {
        if (IsEnabled()) WriteEvent(EvtPageStackTrace, stack_trace);
    }
}

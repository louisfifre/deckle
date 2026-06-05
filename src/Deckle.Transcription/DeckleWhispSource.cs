using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

// Whisp module provider. Covers the transcription engine (TranscriptionEngine),
// the native Whisper model lifecycle (lazy load, idle unload), boot warmup,
// transcription itself (params, prompt, segments, completion), clipboard,
// paste, whisper.cpp log redirection, and structured end-of-pipeline heartbeats
// (LatencyRecorded, CorpusAsrRecorded, CorpusRewriteRecorded). Module settings
// persistence goes through the four transitional Settings* events (documented
// exception: see DeckleAudioSource).
//
// Design choices: notable exceptions:
//
//   1. The old narrative lines (`_log.Narrative`) are abandoned. EventLevel has
//      no "Narrative" level; these lines were prose reformulations that carried
//      no new information beyond the Info milestones and structured verbose
//      events preceding them.
//
//   2. LatencyRecorded and CorpusRecorded are canonical JSONL events (filtered
//      by TelemetryListenerBootstrap). The three human [DONE] timings /
//      llm_metrics / outputs lines are still emitted in parallel under
//      PipelineTimings, PipelineLlmMetrics, and PipelineOutputs: this is the
//      human reading path on the LogWindow side. JsonlEventListener latency.jsonl
//      only picks up LatencyRecorded (24 flattened fields), not the three human
//      lines.
//
//   3. UserFeedback is emitted through the canonical
//      UserFeedbackEmitted(severity, title, body, role) channel: severity
//      0/1/2 = Info/Warning/Error, role 0/1 = Replacement/Overlay. The mapping
//      lives on the HudFeedbackEventListener side.
[EventSource(Name = "Deckle.Whisp")]
public sealed partial class DeckleWhispSource : DeckleEventSource
{
    public static readonly DeckleWhispSource Log = new();

    private DeckleWhispSource() { }

    // ── EventIds: sequential from 1, never reused ───────────────────────
    public const int EvtWarmupClipMissing                = 1;
    public const int EvtWarmupClipHeaderInvalid          = 2;
    public const int EvtWarmupClipSampleMismatch         = 3;
    public const int EvtWarmupClipLoadFailed             = 4;
    public const int EvtWarmupStart                      = 5;
    // 6–9 — EvtWarmupCancelledBeforeModel / AbortedModelLoad /
    // CancelledBeforeTranscribe / CancelledDuringTranscribe removed with the
    // move from boot warmup to on-demand prime (synchronous on the worker,
    // without distinct cancellation phases). IDs burned, never reused.
    public const int EvtWarmupComplete                   = 10;
    // 11–16 — EvtWarmupCompleteDetail / Failed / FlagModelKO / FlagOllamaKO /
    // FlagOllamaRecovered / FlagMicKO removed with the same move (no more
    // warmup flags consumed on first hotkey). IDs burned, never reused.
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
    // 77: EvtCorpusRecorded removed in favor of CorpusAsr/RewriteRecorded
    // (ADR-0006). The ID is burned, never reused.
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
    public const int EvtTranscriptionPreprocessed        = 107;
    public const int EvtStreamingDrained                 = 108;
    public const int EvtUtteranceSkipped                 = 109;
    public const int EvtPreprocessedTelemetryRecorded    = 110;


}

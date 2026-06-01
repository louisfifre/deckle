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
public sealed partial class DeckleWhispSource : DeckleEventSource
{
    public static readonly DeckleWhispSource Log = new();

    private DeckleWhispSource() { }

    // ── EventIds — séquentiels à partir de 1, jamais réutilisés ─────────
    public const int EvtWarmupClipMissing                = 1;
    public const int EvtWarmupClipHeaderInvalid          = 2;
    public const int EvtWarmupClipSampleMismatch         = 3;
    public const int EvtWarmupClipLoadFailed             = 4;
    public const int EvtWarmupStart                      = 5;
    // 6–9 — EvtWarmupCancelledBeforeModel / AbortedModelLoad /
    // CancelledBeforeTranscribe / CancelledDuringTranscribe retirés avec le
    // passage du warmup boot au prime à la demande (synchrone sur le worker,
    // sans phases d'annulation distinctes). IDs brûlés, jamais réutilisés.
    public const int EvtWarmupComplete                   = 10;
    // 11–16 — EvtWarmupCompleteDetail / Failed / FlagModelKO / FlagOllamaKO /
    // FlagOllamaRecovered / FlagMicKO retirés avec le même passage (plus de
    // flags warmup consommés au premier hotkey). IDs brûlés, jamais réutilisés.
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
    // 77 — EvtCorpusRecorded retiré au profit de CorpusAsr/RewriteRecorded
    // (ADR-0006). L'ID est brûlé, jamais réutilisé.
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


}

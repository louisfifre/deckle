using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

// Whisp module provider. Covers the transcription engine (TranscriptionEngine),
// the model lifecycle as the engine sees it (lazy load, idle unload), the
// cold-start prime, the take and file pipelines, clipboard, paste, and
// structured end-of-pipeline heartbeats (LatencyRecorded, CorpusAsrRecorded,
// CorpusRewriteRecorded). The native Whisper backend has its own provider
// (DeckleWhisperSource).
//
// Transcript text never enters an operational event, so never the LogWindow or
// app.jsonl. It travels only in the Dataset-tagged corpus events, which those
// sinks reject and which the corpus consent gates.
//
// PipelineTimings, PipelineLlmMetrics and PipelineOutputs repeat LatencyRecorded
// for the LogWindow reader; latency.jsonl takes only LatencyRecorded.
[EventSource(Name = "Deckle-Whisp")]
public sealed partial class DeckleWhispSource : DeckleEventSource
{
    public static readonly DeckleWhispSource Log = new();

    private DeckleWhispSource() { }

    // Per-take detail follows the Transcription verbosity control.
    private bool IsTakeDetailEnabled(EventKeywords keywords)
        => OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Transcription, this, EventLevel.Verbose, keywords);

    // ── EventIds: sequential from 1, never reused ───────────────────────
    public const int EvtWarmupClipMissing                = 1;
    public const int EvtWarmupClipHeaderInvalid          = 2;
    public const int EvtWarmupClipSampleMismatch         = 3;
    // 4 retired.
    public const int EvtWarmupStart                      = 5;
    // 6–9: retired IDs, never reused.
    public const int EvtWarmupComplete                   = 10;
    // 11–16: retired IDs, never reused.
    public const int EvtModelLoading                     = 17;
    // 18 retired.
    // 19 retired.
    public const int EvtModelInitFromFile                = 20;
    // 21 retired.
    public const int EvtModelLoaded                      = 22;
    // 23 retired.
    public const int EvtModelOnDemandLoad                = 24;
    public const int EvtModelIdleUnloadSkipped           = 25;
    public const int EvtModelUnloadedJalon               = 26;
    public const int EvtModelUnloaded                    = 27;
    public const int EvtModelIdleTimerSet                = 28;
    public const int EvtModelPathEnvIgnored              = 29;
    // 30 retired.
    // 31 retired.
    // 32 retired.
    // 33 retired.
    // 34: retired ID, never reused.
    public const int EvtHotkeyToggleIgnored              = 35;
    public const int EvtHotkeyStartingCASLost            = 36;
    // 37 retired.
    // 38 retired.
    public const int EvtRecordingLowAudio                = 39;
    public const int EvtAutoCalibrated                   = 40;
    // 41 retired.
    public const int EvtTranscribeStarted                = 42;
    public const int EvtTranscribeStartDetail            = 43;
    public const int EvtTranscribeParams                 = 44;
    // 45 retired.
    public const int EvtTranscribeEmpty                  = 46;
    // 47 retired.
    public const int EvtTranscribeCompleted              = 48;
    public const int EvtTranscribeCompleteDetail         = 49;
    public const int EvtTranscribeRepetitionLoop         = 50;
    public const int EvtTranscribeSkipped                = 51;
    // 52 retired.
    // 53 retired.
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
    // 72 retired.
    // 73 retired.
    public const int EvtPipelineLlmMetrics               = 74;
    public const int EvtPipelineOutputs                  = 75;
    public const int EvtLatencyRecorded                  = 76;
    // 77: retired ID, never reused.
    public const int EvtUserFeedbackEmitted              = 78;
    public const int EvtManualProfileNotFound            = 79;
    public const int EvtDisposeWorkerJoinTimeout         = 80;
    public const int EvtDisposeWorkerJoined              = 81;
    public const int EvtDisposeStart                     = 82;
    // 83 retired.
    // 84 retired.
    // 85 retired.
    // 86 retired.
    // 87 retired.
    public const int EvtPageInitStart                    = 88;
    public const int EvtPageInitComplete                 = 89;
    // 90 retired.
    // 91 retired.
    public const int EvtPageLoadedStart                  = 92;
    public const int EvtPageReady                        = 93;
    public const int EvtPageLoadedComplete               = 94;
    // 95 retired.
    // 96 retired.
    public const int EvtPageDiscardRestartChanges        = 97;
    public const int EvtPageResetAll                     = 98;
    public const int EvtPageStackTrace                   = 99;
    // 100 retired.
    public const int EvtWhisperInitParamsParsed          = 101;
    public const int EvtWhisperModelLoadParsed           = 102;
    public const int EvtWhisperBackendInitParsed         = 103;
    public const int EvtWhisperInitStateParsed           = 104;
    public const int EvtCorpusAsrRecorded                = 105;
    public const int EvtCorpusRewriteRecorded            = 106;
    public const int EvtTranscriptionPreprocessed        = 107;
    public const int EvtStreamingDrained                 = 108;
    // 109 retired.
    public const int EvtPreprocessedTelemetryRecorded    = 110;
    // 111 retired.
    // 112 retired.
    public const int EvtConsumerUtterance                = 113;
    public const int EvtStreamingPipelineStarted         = 114;
    public const int EvtSegmenterSettingsSnapshot        = 115;
    public const int EvtSegmenterBlipDropped             = 116;
    // 117–124: retired IDs, never reused.

    // ── Verbose mirrors ─────────────────────────────────────────────────
    // A *Detail event carries the k=v detail of the milestone it mirrors, so
    // the milestone message stays a plain sentence. IDs are public in the ETW
    // manifest; never reuse an id.
    public const int EvtWarmupClipHeaderInvalidDetail    = 125;
    public const int EvtWarmupClipSampleMismatchDetail   = 126;
    // 127 retired.
    // 128 retired.
    // 129 retired.
    // 130 retired.
    public const int EvtModelPathEnvIgnoredDetail        = 131;
    // 132 retired.
    // 133 retired.
    // 134 retired.
    // 135 retired.
    // 136 retired.
    public const int EvtAutoCalibratedDetail             = 137;
    // 138 retired.
    // 139 retired.
    // 140 retired.
    // 141 retired.
    // 142 retired.
    // 143 retired.
    public const int EvtClipboardAllocFailedDetail       = 144;
    public const int EvtClipboardVerifyMismatchDetail    = 145;
    public const int EvtPasteSendInputPartialDetail      = 146;
    public const int EvtPipelineCompletedDetail          = 147;
    public const int EvtManualProfileNotFoundDetail      = 148;
    public const int EvtDisposeWorkerJoinTimeoutDetail   = 149;
    // 150 retired.
    // 151 retired.
    // 152 retired.
    public const int EvtTranscribeHallucinationFiltered  = 153;
    // 154 retired.
    public const int EvtPrimeOverlap                     = 155;
    public const int EvtFileTranscriptionStarted         = 156;
    // 157 retired.
    public const int EvtFileTranscriptionSaved           = 158;
    public const int EvtFileTranscriptionSavedDetail     = 159;
    // 160 retired.
    // 161 retired.
    // 162 retired.
    // 163 retired.
    // 164 retired.
    public const int EvtModelFallback                    = 165;
    public const int EvtModelFallbackDetail              = 166;
    public const int EvtTranscriptionCorrelation         = 167;

    public const int EvtTranscribePromptConfigured       = 168;
    public const int EvtTranscribeRepetitionLoopMetrics  = 169;
    public const int EvtSegmentRecognized                = 170;
    public const int EvtDictationPasted                  = 171;
    public const int EvtDictationCopied                  = 172;
    public const int EvtFileTranscriptionCompleted       = 173;
    // 174 retired.
    // 175 retired.
    // 176 retired.
    public const int EvtMicrophoneRecovered              = 177;
    // 178 retired.
    // 179 retired.
    public const int EvtModelRecovered                   = 180;
    public const int EvtFileTranscriptionBatchStarted    = 181;
    public const int EvtFileTranscriptionBatchStartedDetail = 182;
    public const int EvtFileTranscriptionBatchCompleted  = 183;
    public const int EvtFileTranscriptionBatchCompletedDetail = 184;
    // 185 retired.
    // 186 retired.
    // 187 retired.
    // 188 retired.
    // 189 retired.
    // 190 retired.
    // 191 retired.
    // 192 retired.
    public const int EvtTranscribeHallucinationFilteredMetrics = 193;
    public const int EvtWarmupClipMissingDetail          = 194;
    public const int EvtModelUnavailable                 = 195;
    public const int EvtModelLoadRetryFailed             = 196;
    public const int EvtWarmupFailed                     = 197;
    public const int EvtWarmupCancelled                  = 198;
    public const int EvtModelIdleUnloadFailed            = 199;
    public const int EvtTeardownExceptionIgnored         = 200;
    public const int EvtWorkerStarted                    = 201;
    public const int EvtWorkerStopped                    = 202;
    public const int EvtPipelineCrashed                  = 203;
    public const int EvtTranscriptionFailed              = 204;
    public const int EvtTakeDropped                      = 205;
    public const int EvtUtterancesDiscarded              = 206;
    public const int EvtUtteranceSkipped                 = 207;
    public const int EvtUtteranceSkippedRepeat           = 208;
    public const int EvtUtterancesRecovered              = 209;
    public const int EvtFileTranscriptionBatchAborted    = 210;
    public const int EvtFileTranscriptionFailed          = 211;
    public const int EvtFileDecodeFailed                 = 212;
    public const int EvtFileTranscriptionStartedDetail   = 213;
    public const int EvtFileTranscriptionEmpty           = 214;
    public const int EvtFileQueueChanged                 = 215;
    public const int EvtCorpusAudioWriteFailed           = 216;
    public const int EvtCorpusAudioWriteRetryFailed      = 217;
    public const int EvtCorpusAudioWriteRecovered        = 218;
    public const int EvtDictationRecoveryWriteFailed     = 219;
    public const int EvtDictationRecoveryWriteRetryFailed = 220;
    public const int EvtDictationRecoveryWriteRecovered  = 221;
    public const int EvtDictationRecoveryReadFailed      = 222;
    public const int EvtDictationRecoveryDeleteFailed    = 223;
    public const int EvtRewriteFellBackToRaw             = 224;
    public const int EvtDictationNotDelivered            = 225;
    public const int EvtMicrophoneUnavailable            = 226;
    public const int EvtSegmenterUtteranceEmitted        = 227;
    public const int EvtStreamingHeartbeat               = 228;
    public const int EvtStreamingDrainedDetail           = 229;
    public const int EvtWarmupClipLoadFailed             = 230;
    public const int EvtFileTranscriptionWriteFailed     = 231;
    public const int EvtPageInitFailed                   = 232;
    public const int EvtPageLoadedFailed                 = 233;
    public const int EvtPageModelScanFailed              = 234;
    public const int EvtPipelineTimings                  = 235;
    public const int EvtSettingsLoadWarning              = 236;
    public const int EvtSettingsLoadError                = 237;

    // ── Per-provider keywords ───────────────────────────────────────────
    // Bits 0x400+ are provider-local (Deckle.Diagnostics.Keywords). No live
    // event uses this bit; it stays burned because ETW keyword contracts are
    // never recycled.
    public const EventKeywords KwTranscript = (EventKeywords)0x400;
}

using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

// LLM module provider, shared with Deckle.Llm.Rewrite. Covers the generation
// engines (Ollama, chat-completions, Claude) and the rewrite service above
// them, the paragraph offer coordinator, the Settings → LLM surface (LlmPage)
// and the Ollama administration calls (OllamaService). Module settings
// persistence (LlmSettingsService) goes through the four Settings* events.
//
// Verbose/Info separation per the deckle-logging skill: a milestone is a
// short sentence a person reads. A Warning or Error carries its cause in its
// own payload (ex_type and ex_message, a reason word, the HTTP status), so a
// log that keeps only Warnings and Errors still says why; its Verbose
// mirror, when it has one, keeps the long extras (JSON previews).
//
// Rewrite milestones belong to RewriteService, which sees every exit path;
// the engines only add the Verbose request and response detail of their
// transport. Only the transcript rewrite of a take is lasting work: it writes
// TranscriptRewriteStarted, then TranscriptRewriteCompleted, RewriteTimeout or
// RewriteUnavailable, all carrying one work_id, and the first and last are in
// the log. The paragraph and sentence rewrites run on every pause in
// typing: their Started, Completed and Cancelled lines are Verbose detail and
// the log sees them as a 60 s count (LoopPulse). Prompt and completion text never enter a payload: counts,
// models, providers and timings only.
//
// The Ollama /api/ps probe exists only to enrich admitted Transcription
// detail, so its whole family answers to that one activity and has no
// milestone of its own: the rewrite milestones carry the outcome.
[EventSource(Name = "Deckle-Llm")]
public sealed partial class DeckleLlmSource : DeckleEventSource
{
    public static readonly DeckleLlmSource Log = new();

    private DeckleLlmSource() { }

    // ── EventIds ─────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest; never reuse an id.
    public const int EvtRewriteSkippedNoModel       = 1;
    // 2 retired.
    public const int EvtRewriteStartedDetail        = 3;
    // 4 retired.
    // 5 retired.
    // 6 retired.
    // 7 retired.
    // 8 retired.
    // 9 retired.
    public const int EvtPsProbeEmpty                = 10;
    // 11 retired.
    // 12 retired.
    public const int EvtUserFeedbackEmitted         = 13;
    // 14 retired.
    // 15 retired.
    // 16 retired.
    // 17 retired.
    // 18 retired.
    // 19 retired.
    // 20 retired.
    public const int EvtEndpointSchemeNotAllowed    = 21;
    public const int EvtEndpointNonLoopbackHost     = 22;
    // 23 retired.
    public const int EvtSettingsLoaded              = 24;
    public const int EvtSettingsLoadComplete        = 25;
    public const int EvtSettingsLoadWarning         = 26;
    public const int EvtSettingsLoadError           = 27;
    public const int EvtRewriteSkippedNoModelDetail = 28;
    // 29 retired.
    // 30 retired.
    // 31 retired.
    // 32 retired.
    // 33 retired.
    // 34 retired.
    // 35 retired.
    // 36 retired.
    // 37 retired.
    // 38 retired.
    // 39 retired.
    // 40 retired.
    public const int EvtEndpointSchemeNotAllowedDetail = 41;
    public const int EvtEndpointNonLoopbackHostDetail  = 42;
    // 43 retired.
    // 44 retired.
    public const int EvtRewriteSkippedNoProviderDetail = 45;
    // 46 retired.
    // 47 retired.
    public const int EvtBatchRewriteSent             = 48;
    public const int EvtBatchRewriteSentDetail       = 49;
    public const int EvtBatchRewriteReturned         = 50;
    public const int EvtBatchRewriteReturnedDetail   = 51;
    // 52 retired.
    // 53 retired.
    public const int EvtRewriteStarted               = 54;
    public const int EvtRewriteCompleted             = 55;
    public const int EvtRewriteCancelled             = 56;
    public const int EvtRewriteResponseDetail        = 57;
    public const int EvtRewriteTimeout               = 58;
    public const int EvtRewriteUnavailable           = 59;
    public const int EvtRewriteAttemptFailed         = 60;
    public const int EvtRewriteRecovered             = 61;
    public const int EvtRewriteSkippedNoProvider     = 62;
    public const int EvtPsProbeRejectedDetail        = 63;
    public const int EvtPsProbeBusyDetail            = 64;
    public const int EvtPsProbeErrorDetail           = 65;
    public const int EvtPsProbeFaulted               = 66;
    public const int EvtBatchRewriteFellBack         = 67;
    public const int EvtPageNavigatedToFailed        = 68;
    public const int EvtEndpointRefreshFailed        = 69;
    public const int EvtManualRefreshFailed          = 70;
    public const int EvtOllamaRefreshSkipped         = 71;
    public const int EvtResetAllFailed               = 72;
    public const int EvtListModelsInvalidJson        = 73;
    public const int EvtListModelsInvalidJsonDetail  = 74;
    public const int EvtShowModelInvalidJson         = 75;
    public const int EvtShowModelInvalidJsonDetail   = 76;
    public const int EvtProviderListModelsFailed     = 77;
    public const int EvtParagraphOfferSettled        = 78;
    public const int EvtParagraphRewriteFailed       = 79;
    public const int EvtProviderKeyReadFailedDetail  = 80;
    public const int EvtTranscriptRewriteStarted     = 81;
    public const int EvtTranscriptRewriteCompleted   = 82;

}

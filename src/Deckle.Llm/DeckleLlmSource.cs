using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

// LLM module provider. Covers transcript rewriting through Ollama (RewriteService),
// the Settings → LLM surface (LlmPage), communication with Ollama's /api/tags,
// /api/show, /api/blobs, /api/create (OllamaService), and orchestrated GGUF
// import (GgufImportDialog). Module settings persistence (LlmSettingsService)
// goes through the four transitional Settings* events.
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info/Warning/
// Error is a short Capital sentence with no IDs and no k=v; the technical detail
// (exception type, model name, http status, durations, JSON preview) lives in a
// Verbose mirror that FOLLOWS the milestone.
[EventSource(Name = "Deckle-Llm")]
public sealed partial class DeckleLlmSource : DeckleEventSource
{
    public static readonly DeckleLlmSource Log = new();

    private DeckleLlmSource() { }

    // ── EventIds ─────────────────────────────────────────────────────────
    public const int EvtRewriteSkippedNoModel       = 1;
    public const int EvtRewriteStarted              = 2;
    public const int EvtRewriteStartedDetail        = 3;
    public const int EvtRewriteCompleted            = 4;
    public const int EvtRewriteCompletedDetail      = 5;
    public const int EvtRewriteMetrics              = 6;
    public const int EvtRewriteTimeout              = 7;
    public const int EvtRewriteUnavailable          = 8;
    public const int EvtPsProbeUnreachable          = 9;
    public const int EvtPsProbeEmpty                = 10;
    public const int EvtOllamaBusy                  = 11;
    public const int EvtPsProbeFailed               = 12;
    public const int EvtUserFeedbackEmitted         = 13;
    public const int EvtPageNavigatedToFailed       = 14;
    public const int EvtEndpointRefreshFailed       = 15;
    public const int EvtManualRefreshFailed         = 16;
    public const int EvtOllamaRefreshSkipped        = 17;
    public const int EvtResetAllFailed              = 18;
    public const int EvtListModelsInvalidJson       = 19;
    public const int EvtShowModelInvalidJson        = 20;
    public const int EvtEndpointSchemeNotAllowed    = 21;
    public const int EvtEndpointNonLoopbackHost     = 22;
    public const int EvtGgufImportFailed            = 23;
    public const int EvtSettingsLoaded              = 24;
    public const int EvtSettingsLoadComplete        = 25;
    public const int EvtSettingsLoadWarning         = 26;
    public const int EvtSettingsLoadError           = 27;
    // Verbose mirrors appended for the Verbose/Info separation: each milestone
    // above whose message carried an exception, model name, http status,
    // duration or JSON preview now emits a short Capital sentence, and the
    // technical detail moves to one of these fresh ids. IDs are public in the
    // ETW manifest; never reuse an id.
    public const int EvtRewriteSkippedNoModelDetail = 28;
    public const int EvtRewriteTimeoutDetail        = 29;
    public const int EvtRewriteUnavailableDetail    = 30;
    public const int EvtPsProbeUnreachableDetail    = 31;
    public const int EvtOllamaBusyDetail            = 32;
    public const int EvtPsProbeFailedDetail         = 33;
    public const int EvtPageNavigatedToFailedDetail = 34;
    public const int EvtEndpointRefreshFailedDetail = 35;
    public const int EvtManualRefreshFailedDetail   = 36;
    public const int EvtOllamaRefreshSkippedDetail  = 37;
    public const int EvtResetAllFailedDetail        = 38;
    public const int EvtListModelsInvalidJsonDetail = 39;
    public const int EvtShowModelInvalidJsonDetail  = 40;
    public const int EvtEndpointSchemeNotAllowedDetail = 41;
    public const int EvtEndpointNonLoopbackHostDetail  = 42;
    public const int EvtGgufImportFailedDetail       = 43;

}

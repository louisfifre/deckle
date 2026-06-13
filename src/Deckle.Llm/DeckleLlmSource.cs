using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

// LLM module provider. Covers transcript rewriting through Ollama (LlmService),
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
public sealed class DeckleLlmSource : DeckleEventSource
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

    // ── Rewrite ─────────────────────────────────────────────────────────

    [Event(EvtRewriteSkippedNoModel,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A rewrite was skipped because the profile has no model — set one in Settings → LLM")]
    public void RewriteSkippedNoModel()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteSkippedNoModel);
    }

    [Event(EvtRewriteSkippedNoModelDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite skipped no model | profile={0}")]
    public void RewriteSkippedNoModelDetail(string profile)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteSkippedNoModelDetail, profile);
    }

    [Event(EvtRewriteStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewriting")]
    public void RewriteStarted()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteStarted);
    }

    [Event(EvtRewriteStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "request | chars={0} | model={1} | profile={2} | family={3} | {4}")]
    public void RewriteStartedDetail(int chars, string model, string profile, string family, string options)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteStartedDetail, chars, model, profile, family, options);
    }

    [Event(EvtRewriteCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewrite complete")]
    public void RewriteCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteCompleted);
    }

    [Event(EvtRewriteCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite complete | ms={0} | in_chars={1} | out_chars={2} | profile={3}")]
    public void RewriteCompletedDetail(long ms, int in_chars, int out_chars, string profile)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteCompletedDetail, ms, in_chars, out_chars, profile);
    }

    [Event(EvtRewriteMetrics,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "{0}")]
    public void RewriteMetrics(string metrics_text)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteMetrics, metrics_text);
    }

    [Event(EvtRewriteTimeout,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewrite timed out")]
    public void RewriteTimeout()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteTimeout);
    }

    [Event(EvtRewriteTimeoutDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite timeout | cap_min={0:F0} | profile={1} | model={2}")]
    public void RewriteTimeoutDetail(double cap_min, string profile, string model)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteTimeoutDetail, cap_min, profile, model);
    }

    [Event(EvtRewriteUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewriter unavailable")]
    public void RewriteUnavailable()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteUnavailable);
    }

    [Event(EvtRewriteUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite unavailable | error={0} | message={1} | profile={2} | model={3}")]
    public void RewriteUnavailableDetail(string ex_type, string message, string profile, string model)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteUnavailableDetail, ex_type, message, profile, model);
    }

    // ── Ollama /api/ps polling ──────────────────────────────────────────

    [Event(EvtPsProbeUnreachable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "The model status probe could not be reached — the model may have crashed")]
    public void PsProbeUnreachable()
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeUnreachable);
    }

    [Event(EvtPsProbeUnreachableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ps probe unreachable | http={0}")]
    public void PsProbeUnreachableDetail(int http_status)
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeUnreachableDetail, http_status);
    }

    // Constant hint (no resident model, request may be stuck) lived in the old
    // k=v message; the method takes no args, so the milestone carries no detail
    // and no Verbose mirror is needed.
    [Event(EvtPsProbeEmpty,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "The model status probe found no resident model — the request may be stuck")]
    public void PsProbeEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeEmpty);
    }

    [Event(EvtOllamaBusy,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "Ollama is busy with another model")]
    public void OllamaBusy()
    {
        if (IsEnabled()) WriteEvent(EvtOllamaBusy);
    }

    [Event(EvtOllamaBusyDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ollama busy | name={0} | vram_gb={1:F1} | unload={2} | waited_sec={3:F0} | cap_min={4:F0}")]
    public void OllamaBusyDetail(string name, double vram_gb, string unload_suffix, double waited_seconds, double cap_minutes)
    {
        if (IsEnabled()) WriteEvent(EvtOllamaBusyDetail, name, vram_gb, unload_suffix, waited_seconds, cap_minutes);
    }

    [Event(EvtPsProbeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "The model status probe failed")]
    public void PsProbeFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeFailed);
    }

    [Event(EvtPsProbeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ps probe failed | error={0} | message={1}")]
    public void PsProbeFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeFailedDetail, ex_type, message);
    }

    // ── UserFeedback (HUD bridge) ───────────────────────────────────────
    // Canonical event consumed by HudFeedbackEventListener (filter on
    // event name). severity 0/1/2 = Info/Warning/Error,
    // role 0/1 = Replacement/Overlay.

    [Event(EvtUserFeedbackEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{1}: {2}")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }

    // ── LlmPage UI failures ─────────────────────────────────────────────

    [Event(EvtPageNavigatedToFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The LLM settings page failed to open")]
    public void PageNavigatedToFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPageNavigatedToFailed);
    }

    [Event(EvtPageNavigatedToFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page navigated-to failed | error={0} | message={1}")]
    public void PageNavigatedToFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtPageNavigatedToFailedDetail, ex_type, message);
    }

    [Event(EvtEndpointRefreshFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Endpoint refresh failed")]
    public void EndpointRefreshFailed()
    {
        if (IsEnabled()) WriteEvent(EvtEndpointRefreshFailed);
    }

    [Event(EvtEndpointRefreshFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "endpoint refresh failed | error={0} | message={1}")]
    public void EndpointRefreshFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtEndpointRefreshFailedDetail, ex_type, message);
    }

    [Event(EvtManualRefreshFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Manual refresh failed")]
    public void ManualRefreshFailed()
    {
        if (IsEnabled()) WriteEvent(EvtManualRefreshFailed);
    }

    [Event(EvtManualRefreshFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "manual refresh failed | error={0} | message={1}")]
    public void ManualRefreshFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtManualRefreshFailedDetail, ex_type, message);
    }

    [Event(EvtOllamaRefreshSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ollama refresh was skipped")]
    public void OllamaRefreshSkipped()
    {
        if (IsEnabled()) WriteEvent(EvtOllamaRefreshSkipped);
    }

    [Event(EvtOllamaRefreshSkippedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ollama refresh skipped | error={0} | message={1}")]
    public void OllamaRefreshSkippedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtOllamaRefreshSkippedDetail, ex_type, message);
    }

    [Event(EvtResetAllFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Resetting the LLM settings failed")]
    public void ResetAllFailed()
    {
        if (IsEnabled()) WriteEvent(EvtResetAllFailed);
    }

    [Event(EvtResetAllFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reset all failed | error={0} | message={1}")]
    public void ResetAllFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtResetAllFailedDetail, ex_type, message);
    }

    // ── OllamaService ───────────────────────────────────────────────────

    [Event(EvtListModelsInvalidJson,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Ollama returned invalid JSON while listing models")]
    public void ListModelsInvalidJson()
    {
        if (IsEnabled()) WriteEvent(EvtListModelsInvalidJson);
    }

    [Event(EvtListModelsInvalidJsonDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "list models invalid json | error={0} | preview={1}")]
    public void ListModelsInvalidJsonDetail(string ex_message, string preview)
    {
        if (IsEnabled()) WriteEvent(EvtListModelsInvalidJsonDetail, ex_message, preview);
    }

    [Event(EvtShowModelInvalidJson,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Ollama returned invalid JSON for a model's details")]
    public void ShowModelInvalidJson()
    {
        if (IsEnabled()) WriteEvent(EvtShowModelInvalidJson);
    }

    [Event(EvtShowModelInvalidJsonDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "show model invalid json | error={0} | model={1} | preview={2}")]
    public void ShowModelInvalidJsonDetail(string ex_message, string model, string preview)
    {
        if (IsEnabled()) WriteEvent(EvtShowModelInvalidJsonDetail, ex_message, model, preview);
    }

    [Event(EvtEndpointSchemeNotAllowed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Ollama endpoint scheme is not allowed — falling back to the default")]
    public void EndpointSchemeNotAllowed()
    {
        if (IsEnabled()) WriteEvent(EvtEndpointSchemeNotAllowed);
    }

    [Event(EvtEndpointSchemeNotAllowedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "endpoint scheme not allowed | scheme={0} | fallback_url={1}")]
    public void EndpointSchemeNotAllowedDetail(string scheme, string fallback_url)
    {
        if (IsEnabled()) WriteEvent(EvtEndpointSchemeNotAllowedDetail, scheme, fallback_url);
    }

    [Event(EvtEndpointNonLoopbackHost,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Ollama endpoint is not loopback — requests will leave this machine, make sure that is intended")]
    public void EndpointNonLoopbackHost()
    {
        if (IsEnabled()) WriteEvent(EvtEndpointNonLoopbackHost);
    }

    [Event(EvtEndpointNonLoopbackHostDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "endpoint non-loopback host | host={0}")]
    public void EndpointNonLoopbackHostDetail(string host)
    {
        if (IsEnabled()) WriteEvent(EvtEndpointNonLoopbackHostDetail, host);
    }

    [Event(EvtGgufImportFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "GGUF import failed")]
    public void GgufImportFailed()
    {
        if (IsEnabled()) WriteEvent(EvtGgufImportFailed);
    }

    [Event(EvtGgufImportFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "gguf import failed | error={0} | message={1}")]
    public void GgufImportFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtGgufImportFailedDetail, ex_type, message);
    }

    // ── Settings persistence (transitoire — voir DeckleAudioSource) ─────
    // Deliberate generic Message="{0}" channel: the JsonSettingsStore<T>
    // delegates in Deckle.Core are Action<string> and call these four methods
    // with a pre-formatted message, so the call site cannot distinguish
    // operations. Typed by level and keyword, not by operation; left
    // byte-identical to its DeckleAudioSource twin per that provider's defended
    // design. The clean redesign comes when JsonSettingsStore moves to a direct
    // EventSource contract. NOT typified — see the alignment report.

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
}

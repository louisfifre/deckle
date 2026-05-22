using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

// LLM module provider. Couvre la réécriture du transcript via Ollama
// (LlmService), la surface Settings → LLM (LlmPage), la communication
// avec /api/tags, /api/show, /api/blobs, /api/create d'Ollama
// (OllamaService), et l'import GGUF orchestré (GgufImportDialog).
// La persistance settings du module (LlmSettingsService) passe par les
// quatre events Settings* transitoires.
[EventSource(Name = "Deckle.Llm")]
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

    // ── Rewrite ─────────────────────────────────────────────────────────

    [Event(EvtRewriteSkippedNoModel,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "profile '{0}' has no model configured — rewrite skipped. Set it in Settings → LLM.")]
    public void RewriteSkippedNoModel(string profile)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteSkippedNoModel, profile);
    }

    [Event(EvtRewriteStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewriting ({0})")]
    public void RewriteStarted(string profile)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteStarted, profile);
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
           Message = "timeout | cap_min={0:F0} | profile={1} | model={2}")]
    public void RewriteTimeout(double cap_min, string profile, string model)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteTimeout, cap_min, profile, model);
    }

    [Event(EvtRewriteUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "unavailable | error={0}: {1} | profile={2} | model={3}")]
    public void RewriteUnavailable(string ex_type, string message, string profile, string model)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteUnavailable, ex_type, message, profile, model);
    }

    // ── Ollama /api/ps polling ──────────────────────────────────────────

    [Event(EvtPsProbeUnreachable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ps probe unreachable | http={0} | hint=model may have crashed")]
    public void PsProbeUnreachable(int http_status)
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeUnreachable, http_status);
    }

    [Event(EvtPsProbeEmpty,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ps probe empty | hint=no resident model, request may be stuck")]
    public void PsProbeEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeEmpty);
    }

    [Event(EvtOllamaBusy,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "Ollama busy — {0} resident ({1:F1} GB{2}). Waited {3:F0}s so far (giving up at {4:F0} min).")]
    public void OllamaBusy(string name, double vram_gb, string unload_suffix, double waited_seconds, double cap_minutes)
    {
        if (IsEnabled()) WriteEvent(EvtOllamaBusy, name, vram_gb, unload_suffix, waited_seconds, cap_minutes);
    }

    [Event(EvtPsProbeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ps probe failed | error={0}: {1}")]
    public void PsProbeFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeFailed, ex_type, message);
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
           Message = "OnNavigatedTo failed: {0}: {1}")]
    public void PageNavigatedToFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtPageNavigatedToFailed, ex_type, message);
    }

    [Event(EvtEndpointRefreshFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Endpoint refresh failed: {0}: {1}")]
    public void EndpointRefreshFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtEndpointRefreshFailed, ex_type, message);
    }

    [Event(EvtManualRefreshFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Manual refresh failed: {0}: {1}")]
    public void ManualRefreshFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtManualRefreshFailed, ex_type, message);
    }

    [Event(EvtOllamaRefreshSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ollama refresh skipped: {0}: {1}")]
    public void OllamaRefreshSkipped(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtOllamaRefreshSkipped, ex_type, message);
    }

    [Event(EvtResetAllFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Reset all failed: {0}: {1}")]
    public void ResetAllFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtResetAllFailed, ex_type, message);
    }

    // ── OllamaService ───────────────────────────────────────────────────

    [Event(EvtListModelsInvalidJson,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "ListModels: invalid JSON from Ollama ({0}) | preview={1}")]
    public void ListModelsInvalidJson(string ex_message, string preview)
    {
        if (IsEnabled()) WriteEvent(EvtListModelsInvalidJson, ex_message, preview);
    }

    [Event(EvtShowModelInvalidJson,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "ShowModel: invalid JSON from Ollama ({0}) | model={1} | preview={2}")]
    public void ShowModelInvalidJson(string ex_message, string model, string preview)
    {
        if (IsEnabled()) WriteEvent(EvtShowModelInvalidJson, ex_message, model, preview);
    }

    [Event(EvtEndpointSchemeNotAllowed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ollama endpoint scheme \"{0}\" is not allowed. Falling back to {1}.")]
    public void EndpointSchemeNotAllowed(string scheme, string fallback_url)
    {
        if (IsEnabled()) WriteEvent(EvtEndpointSchemeNotAllowed, scheme, fallback_url);
    }

    [Event(EvtEndpointNonLoopbackHost,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ollama endpoint host \"{0}\" is not loopback. Requests will leave this machine. Make sure that is intended.")]
    public void EndpointNonLoopbackHost(string host)
    {
        if (IsEnabled()) WriteEvent(EvtEndpointNonLoopbackHost, host);
    }

    [Event(EvtGgufImportFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "GGUF import failed: {0}: {1}")]
    public void GgufImportFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtGgufImportFailed, ex_type, message);
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
}

using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
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

}

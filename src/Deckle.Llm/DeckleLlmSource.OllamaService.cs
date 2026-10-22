using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── OllamaService ───────────────────────────────────────────────────

    [Event(EvtListModelsInvalidJson,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Ollama returned invalid JSON while listing models | ex_type={0} | ex_message={1}")]
    public void ListModelsInvalidJson(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtListModelsInvalidJson, ex_type, ex_message);
    }

    // The first 200 characters of Ollama's answer: model names and sizes,
    // never prompt or completion text.
    [Event(EvtListModelsInvalidJsonDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "list models invalid json | preview={0}")]
    public void ListModelsInvalidJsonDetail(string preview)
    {
        if (IsEnabled()) WriteEvent(EvtListModelsInvalidJsonDetail, preview);
    }

    [Event(EvtShowModelInvalidJson,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Ollama returned invalid JSON for a model's details | model={0} | ex_type={1} | ex_message={2}")]
    public void ShowModelInvalidJson(string model, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtShowModelInvalidJson, model, ex_type, ex_message);
    }

    [Event(EvtShowModelInvalidJsonDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "show model invalid json | preview={0}")]
    public void ShowModelInvalidJsonDetail(string preview)
    {
        if (IsEnabled()) WriteEvent(EvtShowModelInvalidJsonDetail, preview);
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

}

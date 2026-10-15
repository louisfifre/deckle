using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── ProviderModelCatalog ────────────────────────────────────────────

    [Event(EvtProviderListModelsFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A provider did not answer the model listing")]
    public void ProviderListModelsFailed()
    {
        if (IsEnabled()) WriteEvent(EvtProviderListModelsFailed);
    }

    [Event(EvtProviderListModelsFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "provider list models failed | provider={0} | error={1} | message={2}")]
    public void ProviderListModelsFailedDetail(string provider, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtProviderListModelsFailedDetail, provider, ex_type, message);
    }
}

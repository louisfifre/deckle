using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── ProviderModelCatalog ────────────────────────────────────────────
    //
    // provider is the name the person gave it in Settings, which is what they
    // will look for; the cause rides in the same line.

    [Event(EvtProviderListModelsFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A provider did not answer the model listing | provider={0} | ex_type={1} | ex_message={2}")]
    public void ProviderListModelsFailed(string provider, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtProviderListModelsFailed, provider, ex_type, ex_message);
    }

    // The vault refused to read a provider's API key; the listing goes on
    // without one. The key itself never enters the payload.
    [Event(EvtProviderKeyReadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "provider key read failed | provider={0} | ex_type={1} | ex_message={2}")]
    public void ProviderKeyReadFailedDetail(string provider, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtProviderKeyReadFailedDetail, provider, ex_type, ex_message);
    }
}

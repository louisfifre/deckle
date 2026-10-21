using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // A WinRT network query threw; NetworkStatusChanged then reports the
    // fallback value. query: connection_profile, connectivity_level,
    // host_names.
    [Event(EvtNetworkQueryFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "network query failed | query={0} | ex_type={1} | ex_message={2}")]
    public void NetworkQueryFailed(string query, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtNetworkQueryFailed, query, ex_type, ex_message);
    }
}

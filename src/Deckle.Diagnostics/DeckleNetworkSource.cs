using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: machine network state transitions. Capturing
// connectivity (presence / absence, profile, NIC counts) allows correlating
// cross-cutting HTTP failures (Hue REST, Ollama, future LLM services or WLED
// drivers) with an OS-level outage or profile switch instead of looking for the
// cause in the relevant business provider. The primitive is strictly
// non-business and consumed by every module that talks to the network:
// promotion to cross-cutting sub-provider under the two-clause criterion in
// `reference--eventsource-convention--1.2.md`
// §*Cross-cutting sub-providers*.
//
// A single emitter subscribed to `NetworkInformation.NetworkStatusChanged` at
// App boot is enough: the Windows Runtime API already broadcasts to the whole
// process, duplicating the subscription would only duplicate events.
[EventSource(Name = "Deckle.Diagnostics.Network")]
public sealed class DeckleNetworkSource : DeckleEventSource
{
    public static readonly DeckleNetworkSource Log = new();

    private DeckleNetworkSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtNetworkStatusChanged = 1;

    // Network state transition. Emitted once at boot to capture the initial
    // state, then on each NetworkStatusChanged broadcast by Windows.
    // Parameters are flattened and primitive (EventSource constraint):
    //   - connected : true if profile.ConnectivityLevel >= InternetAccess.
    //   - profile   : profile name ("Wi-Fi …", "Ethernet …", "(none)" if no
    //                 active profile).
    //   - ipv4_count / ipv6_count : number of IP hostnames in each family
    //                 (across all interfaces), useful to spot a VPN switch or
    //                 NIC loss.
    [Event(EvtNetworkStatusChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "network status changed | connected={0} | profile={1} | ipv4={2} | ipv6={3}")]
    public void NetworkStatusChanged(bool connected, string profile, int ipv4_count, int ipv6_count)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Network)) return;
        WriteEvent(EvtNetworkStatusChanged, connected, profile, ipv4_count, ipv6_count);
    }
}

using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: machine network state transitions. Capturing
// connectivity (presence / absence, profile, NIC counts) allows correlating
// HTTP failures in any module (Hue REST, Ollama) with an OS-level outage or
// profile switch instead of looking for the cause in the business provider.
//
// Deckle.App's NetworkStatusEmitter is the single emitter: the Windows Runtime
// event already broadcasts to the whole process, so a second subscription
// would only duplicate events.
[EventSource(Name = "Deckle-Network")]
public sealed class DeckleNetworkSource : DeckleEventSource
{
    public static readonly DeckleNetworkSource Log = new();

    private DeckleNetworkSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtNetworkStatusChanged = 1;

    // Emitted once at boot to capture the initial state, then on each
    // NetworkStatusChanged broadcast by Windows.
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

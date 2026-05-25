using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Sub-provider transverse — transitions d'état réseau de la machine.
// Capter la connectivité (présence / absence, profile, comptes NIC)
// permet de corréler les échecs HTTP transverses (Hue REST, Ollama,
// futurs services LLM ou drivers WLED) avec une coupure ou une bascule
// de profil au niveau OS plutôt que de chercher la cause dans le
// provider métier concerné. La primitive est strictement non-métier et
// consommée par tout module qui parle au réseau — promotion en sub-
// provider transverse au sens du critère à deux clauses de la fiche
// `reference--eventsource-convention--1.2.md` §*Sub-providers
// transverses*.
//
// Un seul émetteur abonné à `NetworkInformation.NetworkStatusChanged`
// au boot de l'App suffit — l'API Windows Runtime broadcast déjà à tout
// le process, dupliquer l'abonnement ne ferait que dédoubler les
// événements.
[EventSource(Name = "Deckle.Diagnostics.Network")]
public sealed class DeckleNetworkSource : DeckleEventSource
{
    public static readonly DeckleNetworkSource Log = new();

    private DeckleNetworkSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    public const int EvtNetworkStatusChanged = 1;

    // Transition d'état réseau. Émis une fois au boot pour capturer
    // l'état initial, puis à chaque NetworkStatusChanged broadcast par
    // Windows. Les paramètres sont aplatis et primitifs (contrainte
    // EventSource) :
    //   - connected : true si profile.ConnectivityLevel >= InternetAccess.
    //   - profile   : nom de profil ("Wi-Fi …", "Ethernet …", "(none)"
    //                 si aucun profile actif).
    //   - ipv4_count / ipv6_count : nombre de hostnames IP de chaque
    //                 famille (toutes interfaces confondues), utile pour
    //                 repérer une bascule VPN ou une perte de NIC.
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

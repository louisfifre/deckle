using Deckle.Diagnostics;
using Windows.Networking;
using Windows.Networking.Connectivity;

namespace Deckle.App.Diagnostics;

// Site d'émission unique pour `DeckleNetworkSource`. Câblé au boot
// dans `App.OnLaunched` via `Start()`. L'abonnement à
// `NetworkInformation.NetworkStatusChanged` est statique côté WinRT
// (event broadcast process-wide), donc dupliquer l'abonnement
// dédoublerait les events — `Start()` est idempotent : un appel
// supplémentaire est ignoré.
//
// L'émission initiale au boot capture l'état au moment du lancement —
// sans elle, le premier event ne tomberait qu'à la prochaine transition
// (qui peut ne jamais arriver si la machine reste connectée). Le
// listener boucle alors sur l'état présent en `IsEnabled` puis push.
internal static class NetworkStatusEmitter
{
    private static int _started;

    public static void Start()
    {
        // Interlocked CAS plutôt qu'un bool : `Start()` peut être appelé
        // depuis n'importe quel thread, on veut garantir un seul
        // abonnement sans paying un lock dédié.
        if (System.Threading.Interlocked.Exchange(ref _started, 1) != 0) return;

        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        // Émission initiale — capter l'état au boot.
        EmitCurrent();
    }

    private static void OnNetworkStatusChanged(object sender) => EmitCurrent();

    private static void EmitCurrent()
    {
        // Gate côté provider évite déjà l'allocation du payload quand
        // aucun listener n'écoute, mais la collecte WinRT (GetInternet-
        // ConnectionProfile + GetHostNames) coûte des marshalling
        // COM. Court-circuiter ici quand personne n'écoute économise
        // ces aller-retours.
        if (!DeckleNetworkSource.Log.IsEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Keywords.Network))
        {
            return;
        }

        ConnectionProfile? profile = null;
        try { profile = NetworkInformation.GetInternetConnectionProfile(); }
        catch { /* No internet adapter — profile reste null, traité ci-dessous. */ }

        bool connected = false;
        try
        {
            connected = profile is not null
                && profile.GetNetworkConnectivityLevel() >= NetworkConnectivityLevel.InternetAccess;
        }
        catch { /* WinRT peut throw si le profile est arraché entre Get et GetLevel. */ }

        string profileName = profile?.ProfileName ?? "(none)";

        int ipv4 = 0, ipv6 = 0;
        try
        {
            foreach (HostName host in NetworkInformation.GetHostNames())
            {
                switch (host.Type)
                {
                    case HostNameType.Ipv4: ipv4++; break;
                    case HostNameType.Ipv6: ipv6++; break;
                }
            }
        }
        catch { /* Best-effort — un échec d'énumération laisse les compteurs à 0. */ }

        DeckleNetworkSource.Log.NetworkStatusChanged(connected, profileName, ipv4, ipv6);
    }
}

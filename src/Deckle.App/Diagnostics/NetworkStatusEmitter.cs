using Deckle.Diagnostics;
using Windows.Networking;
using Windows.Networking.Connectivity;

namespace Deckle.App;

// NetworkStatusChanged is a process-wide WinRT event, so a second subscription
// would duplicate every event; Start ignores extra calls. The boot emission
// matters: on a machine that stays connected, no transition ever comes.
internal static class NetworkStatusEmitter
{
    private static int _started;

    public static void Start()
    {
        // Interlocked rather than a bool: one subscription whatever the calling thread.
        if (System.Threading.Interlocked.Exchange(ref _started, 1) != 0) return;

        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        EmitCurrent();
    }

    private static void OnNetworkStatusChanged(object sender) => EmitCurrent();

    private static void EmitCurrent()
    {
        // Checked before the WinRT queries: they cost COM round trips that the
        // provider-side gate would not save.
        if (!DeckleNetworkSource.Log.IsEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Keywords.Network))
        {
            return;
        }

        ConnectionProfile? profile = null;
        try { profile = NetworkInformation.GetInternetConnectionProfile(); }
        catch { /* No internet adapter; profile stays null and is handled below. */ }

        bool connected = false;
        try
        {
            connected = profile is not null
                && profile.GetNetworkConnectivityLevel() >= NetworkConnectivityLevel.InternetAccess;
        }
        catch { /* WinRT can throw if the profile is removed between Get and GetLevel. */ }

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
        catch { /* Best-effort; an enumeration failure leaves counters at 0. */ }

        DeckleNetworkSource.Log.NetworkStatusChanged(connected, profileName, ipv4, ipv6);
    }
}

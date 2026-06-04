using Deckle.Diagnostics;
using Windows.Networking;
using Windows.Networking.Connectivity;

namespace Deckle.App.Diagnostics;

// Single emission site for `DeckleNetworkSource`. Wired at boot in
// `App.OnLaunched` via `Start()`. The subscription to
// `NetworkInformation.NetworkStatusChanged` is static on the WinRT side
// (process-wide broadcast event), so duplicating the subscription would
// duplicate events; `Start()` is idempotent and ignores extra calls.
//
// The initial boot emission captures state at launch time; without it, the
// first event would only arrive on the next transition (which may never happen
// if the machine stays connected). The listener then loops over the current
// state in `IsEnabled` and pushes it.
internal static class NetworkStatusEmitter
{
    private static int _started;

    public static void Start()
    {
        // Interlocked CAS rather than a bool: `Start()` can be called from any
        // thread, and we want to guarantee a single subscription without
        // paying for a dedicated lock.
        if (System.Threading.Interlocked.Exchange(ref _started, 1) != 0) return;

        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        // Initial emission: capture state at boot.
        EmitCurrent();
    }

    private static void OnNetworkStatusChanged(object sender) => EmitCurrent();

    private static void EmitCurrent()
    {
        // The provider-side gate already avoids payload allocation when no
        // listener is attached, but WinRT collection (GetInternet-
        // ConnectionProfile + GetHostNames) costs COM marshalling.
        // Short-circuiting here when nobody listens saves those round trips.
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

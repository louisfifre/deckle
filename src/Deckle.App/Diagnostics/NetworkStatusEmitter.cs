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
        // The profile and its level are read on every broadcast, whatever the
        // Network detail: the tracker writes the journal's connectivity line,
        // which must exist even when nobody asked for the Verbose snapshot.
        ConnectionProfile? profile = null;
        try { profile = NetworkInformation.GetInternetConnectionProfile(); }
        catch (Exception ex)
        {
            // No internet adapter; profile stays null and is handled below.
            DeckleNetworkSource.Log.NetworkQueryFailed(
                "connection_profile", ex.GetType().Name, ex.HResult, ex.Message);
        }

        string state = NetworkConnectivityTracker.StateNone;
        string? profileName = null;
        if (profile is not null)
        {
            try
            {
                state = NetworkConnectivityTracker.StateFromLevel((int)profile.GetNetworkConnectivityLevel());
                profileName = profile.ProfileName;
            }
            catch (Exception ex)
            {
                // WinRT can throw if the profile is removed between Get and
                // GetLevel: the profile is gone, so it reads as none.
                state = NetworkConnectivityTracker.StateNone;
                profileName = null;
                DeckleNetworkSource.Log.NetworkQueryFailed(
                    "connectivity_level", ex.GetType().Name, ex.HResult, ex.Message);
            }
        }

        NetworkConnectivityTracker.Shared.Observe(state, profileName);

        // Checked before the host-name enumeration: it costs COM round trips
        // that the provider-side gate would not save.
        if (!DeckleNetworkSource.Log.IsEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Keywords.Network))
        {
            return;
        }

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
        catch (Exception ex)
        {
            // Best-effort; an enumeration failure leaves counters at 0.
            DeckleNetworkSource.Log.NetworkQueryFailed(
                "host_names", ex.GetType().Name, ex.HResult, ex.Message);
        }

        bool connected = state == NetworkConnectivityTracker.StateInternetAccess;
        DeckleNetworkSource.Log.NetworkStatusChanged(connected, profileName ?? "(none)", ipv4, ipv6);
    }
}

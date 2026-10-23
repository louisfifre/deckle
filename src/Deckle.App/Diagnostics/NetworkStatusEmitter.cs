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
        NetworkQueryIncidents incidents = NetworkQueryIncidents.Shared;

        // The profile and its level are read on every broadcast, whatever the
        // Network detail: the tracker writes the log's connectivity line,
        // which must exist even when nobody asked for the Verbose snapshot.
        //
        // A query that throws is not an offline machine: it is a machine whose
        // state could not be read. The tracker is then left alone, so the
        // log never records a flip to "none" that nobody observed, and the
        // incident says why the line is missing. The next broadcast re-reads.
        ConnectionProfile? profile;
        try
        {
            profile = NetworkInformation.GetInternetConnectionProfile();
            incidents.Succeeded("connection_profile");
        }
        catch (Exception ex)
        {
            incidents.Failed("connection_profile", ex);
            return;
        }

        string state = NetworkConnectivityTracker.StateNone;
        string? profileName = null;
        if (profile is not null)
        {
            try
            {
                state = NetworkConnectivityTracker.StateFromLevel((int)profile.GetNetworkConnectivityLevel());
                // Compared by the tracker, never logged: a Wi-Fi name can
                // identify a place.
                profileName = profile.ProfileName;
                incidents.Succeeded("connectivity_level");
            }
            catch (Exception ex)
            {
                // The profile may be removed between Get and GetLevel. Reading
                // that as "none" would be a guess; the broadcast that removed
                // it is followed by a re-read on the next one.
                incidents.Failed("connectivity_level", ex);
                return;
            }
        }

        NetworkConnectivityTracker.Shared.Observe(state, profileName);

        // Checked before the medium and host-name queries: they cost COM round
        // trips that the provider-side gate would not save.
        if (!DeckleNetworkSource.Log.IsEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Keywords.Network))
        {
            return;
        }

        string medium = profile is null ? NetworkMedium.None : ReadMedium(profile, incidents);

        // -1 says the count is unknown; 0 would say the machine has no address.
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
            incidents.Succeeded("host_names");
        }
        catch (Exception ex)
        {
            ipv4 = ipv6 = -1;
            incidents.Failed("host_names", ex);
        }

        bool connected = state == NetworkConnectivityTracker.StateInternetAccess;
        DeckleNetworkSource.Log.NetworkStatusChanged(connected, medium, ipv4, ipv6);
    }

    private static string ReadMedium(ConnectionProfile profile, NetworkQueryIncidents incidents)
    {
        try
        {
            NetworkAdapter? adapter = profile.NetworkAdapter;
            string medium = adapter is null
                ? NetworkMedium.Other
                : NetworkMedium.FromInterfaceType(adapter.IanaInterfaceType);
            incidents.Succeeded("medium");
            return medium;
        }
        catch (Exception ex)
        {
            incidents.Failed("medium", ex);
            return NetworkMedium.Unknown;
        }
    }
}

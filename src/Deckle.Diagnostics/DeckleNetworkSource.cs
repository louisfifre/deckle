using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: machine network state transitions. Capturing
// connectivity (presence / absence, profile, NIC counts) allows correlating
// HTTP failures in any module (Hue REST, Ollama, Anytype) with an OS-level
// outage or profile switch instead of looking for the cause in the business
// provider.
//
// Deckle.App's NetworkStatusEmitter is the single emitter: the Windows Runtime
// event already broadcasts to the whole process, so a second subscription
// would only duplicate events.
//
// Two layers. `NetworkConnectivityChanged` is the journal line a person reads
// ("the machine went offline at 14:02"), emitted only on a real connectivity
// flip through NetworkConnectivityTracker. `NetworkStatusChanged` stays the
// Verbose snapshot of every OS broadcast, counters included.
//
// Closed `state` vocabulary, from Windows' NetworkConnectivityLevel on the
// internet connection profile (NetworkConnectivityTracker.StateFromLevel):
//   "none"                        — no internet profile, or level None
//   "local_access"                — LAN only
//   "constrained_internet_access" — captive portal or limited internet
//   "internet_access"             — full internet
//   "unknown"                     — before the first observation
[EventSource(Name = "Deckle-Network")]
public sealed class DeckleNetworkSource : DeckleEventSource
{
    public static readonly DeckleNetworkSource Log = new();

    private DeckleNetworkSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest; never reuse an id after deleting an event.
    public const int EvtNetworkStatusChanged      = 1;
    public const int EvtNetworkConnectivityChanged = 2;
    public const int EvtNetworkQueryFailed        = 3;

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

    // One line per connectivity flip, plus the boot state (previous_state=
    // "unknown", reason="startup"), so a journal read on another machine says
    // whether the machine was offline when a call failed. `reason` is what
    // the OS report shows changed, see NetworkConnectivityTracker. The
    // profile name stays out: a Wi-Fi name can identify a place.
    [Event(EvtNetworkConnectivityChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Network | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Network connectivity changed | previous_state={0} | state={1} | reason={2}")]
    public void NetworkConnectivityChanged(string previous_state, string state, string reason)
    {
        if (!IsEnabled(EventLevel.Informational, (EventKeywords)(Keywords.Network | Keywords.Lifecycle))) return;
        WriteEvent(EvtNetworkConnectivityChanged, previous_state, state, reason);
    }

    // A WinRT network query threw. The emitter keeps its best-effort fallback
    // (no profile, zero counters) and records why here. `stage` is the query:
    // "connection_profile", "connectivity_level", "host_names".
    [Event(EvtNetworkQueryFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Network,
           Message = "network query failed | stage={0} | ex_type={1} | hr=0x{2:X8} | ex_message={3}")]
    public void NetworkQueryFailed(string stage, string ex_type, int hr, string ex_message)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Network)) return;
        WriteEvent(EvtNetworkQueryFailed, stage, ex_type, hr, ex_message);
    }
}

// Turns the OS broadcasts into connectivity flips. Windows raises
// NetworkStatusChanged for many reasons that leave connectivity unchanged
// (an address renewal, a second adapter, a cost change), so the journal line
// fires only when the state differs from the last observation.
//
// `reason` vocabulary, derived from what changed in the OS report (the
// foreground WinRT event carries no reason of its own):
//   "startup"          — first observation of the process
//   "profile_lost"     — the internet profile disappeared (adapter down,
//                        cable out, Wi-Fi dropped)
//   "profile_appeared" — an internet profile appeared where there was none
//   "profile_changed"  — another profile now carries the internet route
//   "level_changed"    — same profile, Windows re-rated its connectivity
//
// A profile switch that keeps the same state is not a flip: it stays in the
// Verbose NetworkStatusChanged snapshot.
public sealed class NetworkConnectivityTracker
{
    public static readonly NetworkConnectivityTracker Shared = new();

    public const string StateUnknown = "unknown";
    public const string StateNone = "none";
    public const string StateLocalAccess = "local_access";
    public const string StateConstrainedInternetAccess = "constrained_internet_access";
    public const string StateInternetAccess = "internet_access";

    // Takes the integer value of Windows.Networking.Connectivity
    // .NetworkConnectivityLevel so this module stays free of WinRT types.
    public static string StateFromLevel(int level) => level switch
    {
        0 => StateNone,
        1 => StateLocalAccess,
        2 => StateConstrainedInternetAccess,
        3 => StateInternetAccess,
        _ => StateUnknown,
    };

    private readonly object _gate = new();
    private string? _state;
    private string? _profile;

    // `profileName` is null when no internet profile exists; it is compared,
    // never logged. Returns true when the observation was a flip.
    public bool Observe(string state, string? profileName)
    {
        // Emitted under the lock: two broadcasts handled on two pool threads
        // must reach the journal in the order the state changed.
        lock (_gate)
        {
            string previous = _state ?? StateUnknown;
            string reason;
            if (_state is null)
            {
                reason = "startup";
            }
            else if (_state == state)
            {
                _profile = profileName;
                return false;
            }
            else if (_profile is not null && profileName is null)
            {
                reason = "profile_lost";
            }
            else if (_profile is null && profileName is not null)
            {
                reason = "profile_appeared";
            }
            else if (!string.Equals(_profile, profileName, StringComparison.Ordinal))
            {
                reason = "profile_changed";
            }
            else
            {
                reason = "level_changed";
            }

            _state = state;
            _profile = profileName;
            DeckleNetworkSource.Log.NetworkConnectivityChanged(previous, state, reason);
            return true;
        }
    }
}

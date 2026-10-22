using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

// Lighting module provider, the Hue driver's events. The provider is unique
// for the whole module: another driver (WLED, DMX, Home Assistant) adds its
// events here instead of creating a child Deckle.Lighting.* provider.
[EventSource(Name = "Deckle-Lighting")]
public sealed partial class DeckleLightingSource : DeckleEventSource
{
    public static readonly DeckleLightingSource Log = new();

    private DeckleLightingSource() { }

    // IDs are public in the ETW manifest; never reuse an id after deleting an
    // event. A new event takes the next free id.

    // ── EventIds — discovery ────────────────────────────────────────────
    public const int EvtDiscoveryStarted          = 1;
    public const int EvtDiscoveryStartedDetail    = 2;
    public const int EvtDiscoveryFound            = 3;
    public const int EvtDiscoveryBridgeFound      = 4;
    // 5 retired.

    // ── EventIds — pairing ──────────────────────────────────────────────
    public const int EvtPairingStarted            = 6;
    public const int EvtPairingStartedDetail      = 7;
    public const int EvtBridgePaired              = 8;
    public const int EvtBridgePairedDetail        = 9;
    public const int EvtPairingWaiting            = 10;
    // 11 retired.
    // 12 retired.
    // 13 retired.
    // 14 retired.

    // ── EventIds — groups ───────────────────────────────────────────────
    public const int EvtListingGroups             = 15;
    public const int EvtBridgeReturnedNoGroups    = 16;
    public const int EvtGroupsListed              = 17;
    public const int EvtGroupListed               = 18;

    // ── EventIds — color push ───────────────────────────────────────────
    // 19 retired.
    public const int EvtPushColorOff              = 20;
    public const int EvtPushColor                 = 21;

    // ── EventIds — entertainment ────────────────────────────────────────
    public const int EvtListingEntertainmentConfigs = 22;
    public const int EvtEntertainmentEmpty        = 23;
    public const int EvtEntertainmentV2Catalog    = 24;
    public const int EvtEntertainmentListed       = 25;
    public const int EvtEntertainmentArea         = 26;
    public const int EvtPlacementListed           = 27;
    // 28 retired.

    // ── EventIds — identify ─────────────────────────────────────────────
    // 29 retired.
    public const int EvtLightIdentified           = 30;

    // ── EventIds — lights listing ───────────────────────────────────────
    public const int EvtListingLightsInGroup      = 31;
    public const int EvtLightsListedEmpty         = 32;
    public const int EvtBridgeReturnedNoLights    = 33;
    public const int EvtLightsListed              = 34;
    public const int EvtLightListed               = 35;

    // ── EventIds — v2 id maps + EventStream ─────────────────────────────
    public const int EvtFetchingV2IdMaps          = 36;
    public const int EvtV2IdMapsFetched           = 37;
    public const int EvtEventStreamStarting       = 38;
    public const int EvtEventStreamReconnecting   = 39;
    // 40 retired.

    // ── EventIds — appended ─────────────────────────────────────────────
    public const int EvtDiscoveryFoundDetail        = 41;
    // 42 retired.
    public const int EvtBridgePairedDetail2         = 43;
    // 44 retired.
    // 45 retired.
    // 46 retired.
    // 47 retired.
    // 48 retired.
    // 49 retired.
    public const int EvtListingLightsInGroupDetail  = 50;
    public const int EvtBridgeReturnedNoLightsDetail = 51;
    // 52 retired.
    // 53 retired.
    public const int EvtEntertainmentStreamingStarting = 54;
    public const int EvtEntertainmentStreamingStartingDetail = 55;
    // 56 retired.
    public const int EvtEntertainmentStreamPrimed = 57;
    public const int EvtEntertainmentTransportConnecting = 58;
    public const int EvtEntertainmentPrePrimeStarting = 59;
    public const int EvtEntertainmentPrePrimeDetail = 60;
    // 61 retired.
    // 62 retired.
    public const int EvtLocalDiscoveryStarted = 63;
    public const int EvtLocalDiscoveryStartedDetail = 64;
    public const int EvtLocalDiscoveryCompleted = 65;
    public const int EvtLocalDiscoveryCompletedDetail = 66;
    // 67 retired.
    // 68 retired.
    // 69 retired.
    // 70 retired.
    public const int EvtEventStreamRecovered = 71;
    public const int EvtEventStreamRecoveryDetail = 72;
    public const int EvtDiscoveryFailed = 73;
    public const int EvtLocalDiscoveryFailed = 74;
    public const int EvtPairingRejected = 75;
    public const int EvtBridgeUnreachable = 76;
    public const int EvtPairingHttpError = 77;
    public const int EvtPairingTimedOut = 78;
    public const int EvtSetColorFailed = 79;
    public const int EvtClipV2RequestFailed = 80;
    public const int EvtIdentifyFailed = 81;
    public const int EvtEntertainmentRestFallback = 82;
    public const int EvtEntertainmentPrePrimeFailed = 83;
    public const int EvtEventStreamIncident = 84;
    public const int EvtEventStreamStopped = 85;
    public const int EvtEntertainmentTransportConnected = 86;
    public const int EvtEntertainmentConnectFailed = 87;
    public const int EvtEntertainmentConnectCancelled = 88;
    public const int EvtEntertainmentSessionClosed = 89;
    public const int EvtEntertainmentStreamingStopFailed = 90;
    public const int EvtLightOutputSelected = 91;
    public const int EvtHueStepFailedDetail = 92;

    // Lighting is a supporting provider: its detail follows the Ambient
    // activity's policy while that workflow runs, and the listener alone
    // outside it (discovery and pairing from the settings page).
    [NonEvent]
    internal bool IsHueDetailEnabled(Keywords keywords)
        => OperationalLogAdmission.IsScopedDetailEnabled(
            OperationalLogActivity.Ambient,
            this,
            EventLevel.Verbose,
            (EventKeywords)keywords);

    // Best-effort cleanups and lookups whose failure must not change the
    // outcome still leave a trace, so a recurring silent failure is findable.
    [Event(EvtHueStepFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Resource),
           Message = "hue step failed | step={0} | ex_type={1} | ex_message={2}")]
    public void HueStepFailedDetail(string step, string ex_type, string ex_message)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline | Keywords.Resource)) return;
        WriteEvent(EvtHueStepFailedDetail, step, ex_type, ex_message);
    }
}

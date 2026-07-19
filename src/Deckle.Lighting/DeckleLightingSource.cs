using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

// Lighting module provider. Covers Hue bridge discovery (cloud endpoint), CLIP
// v1 pairing (link-button), group listing, entertainment configuration listing
// (CLIP v2), lights by group listing, color sending (REST PUT /state or
// /action), and visual identification operations (alert flash).
//
// The module will eventually abstract several drivers (WLED, DMX, HomeAssist).
// For V0 only Hue is implemented; the provider is unique for the whole module,
// and future drivers will add their events under the same provider instead of
// creating a child Deckle.Lighting.* provider.
[EventSource(Name = "Deckle-Lighting")]
public sealed partial class DeckleLightingSource : DeckleEventSource
{
    public static readonly DeckleLightingSource Log = new();

    private DeckleLightingSource() { }

    // IDs are public in the ETW manifest; never reuse an id after deleting an
    // event. Milestones keep their original id; the Verbose mirrors added for the
    // Verbose/Info separation take fresh ids 41-51 appended after the sequence.

    // ── EventIds — discovery ────────────────────────────────────────────
    public const int EvtDiscoveryStarted          = 1;
    public const int EvtDiscoveryStartedDetail    = 2;
    public const int EvtDiscoveryFound            = 3;
    public const int EvtDiscoveryBridgeFound      = 4;
    public const int EvtDiscoveryFailed           = 5;

    // ── EventIds — pairing ──────────────────────────────────────────────
    public const int EvtPairingStarted            = 6;
    public const int EvtPairingStartedDetail      = 7;
    public const int EvtBridgePaired              = 8;
    public const int EvtBridgePairedDetail        = 9;
    public const int EvtPairingWaiting            = 10;
    public const int EvtPairingRejected           = 11;
    public const int EvtPairingTimedOut           = 12;
    public const int EvtBridgeUnreachable         = 13;
    public const int EvtPairingHttpError          = 14;

    // ── EventIds — groups ───────────────────────────────────────────────
    public const int EvtListingGroups             = 15;
    public const int EvtBridgeReturnedNoGroups    = 16;
    public const int EvtGroupsListed              = 17;
    public const int EvtGroupListed               = 18;

    // ── EventIds — color push ───────────────────────────────────────────
    public const int EvtSetColorFailed            = 19;
    public const int EvtPushColorOff              = 20;
    public const int EvtPushColor                 = 21;

    // ── EventIds — entertainment ────────────────────────────────────────
    public const int EvtListingEntertainmentConfigs = 22;
    public const int EvtEntertainmentEmpty        = 23;
    public const int EvtEntertainmentV2Catalog    = 24;
    public const int EvtEntertainmentListed       = 25;
    public const int EvtEntertainmentArea         = 26;
    public const int EvtPlacementListed           = 27;
    public const int EvtClipV2GetFailed           = 28;

    // ── EventIds — identify ─────────────────────────────────────────────
    public const int EvtIdentifyFailed            = 29;
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
    public const int EvtEventStreamStopped        = 40;

    // ── EventIds — Verbose mirrors (Verbose/Info separation) ────────────
    // Fresh ids appended after the milestone sequence; each mirrors a milestone
    // whose IDs / k=v detail moved out of the Capital Info message.
    public const int EvtDiscoveryFoundDetail        = 41;
    public const int EvtDiscoveryFailedDetail       = 42;
    public const int EvtBridgePairedDetail2         = 43;
    public const int EvtPairingRejectedDetail       = 44;
    public const int EvtBridgeUnreachableDetail     = 45;
    public const int EvtPairingHttpErrorDetail      = 46;
    public const int EvtSetColorFailedDetail        = 47;
    public const int EvtClipV2GetFailedDetail       = 48;
    public const int EvtIdentifyFailedDetail        = 49;
    public const int EvtListingLightsInGroupDetail  = 50;
    public const int EvtBridgeReturnedNoLightsDetail = 51;
    public const int EvtEntertainmentRestFallback   = 52;
    public const int EvtEntertainmentRestFallbackDetail = 53;
    public const int EvtEntertainmentStreamingStarting = 54;
    public const int EvtEntertainmentStreamingStartingDetail = 55;
    public const int EvtEntertainmentTransportConnected = 56;
    public const int EvtEntertainmentStreamPrimed = 57;
    public const int EvtEntertainmentTransportConnecting = 58;
    public const int EvtEntertainmentPrePrimeStarting = 59;
    public const int EvtEntertainmentPrePrimeDetail = 60;
    public const int EvtEntertainmentPrePrimeFailed = 61;
    public const int EvtEntertainmentPrePrimeFailedDetail = 62;
    public const int EvtLocalDiscoveryStarted = 63;
    public const int EvtLocalDiscoveryStartedDetail = 64;
    public const int EvtLocalDiscoveryCompleted = 65;
    public const int EvtLocalDiscoveryCompletedDetail = 66;
    public const int EvtLocalDiscoveryFailed = 67;
    public const int EvtLocalDiscoveryFailedDetail = 68;
    public const int EvtEventStreamIncident = 69;
    public const int EvtEventStreamIncidentDetail = 70;
    public const int EvtEventStreamRecovered = 71;
    public const int EvtEventStreamRecoveryDetail = 72;

}

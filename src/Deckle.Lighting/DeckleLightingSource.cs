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
public sealed class DeckleLightingSource : DeckleEventSource
{
    public static readonly DeckleLightingSource Log = new();

    private DeckleLightingSource() { }

    private new bool IsEnabled(EventLevel level, EventKeywords keywords)
        => (level != EventLevel.Verbose
            || OperationalLogAdmission.AllowsScopedDetail(OperationalLogActivity.Ambient))
        && base.IsEnabled(level, keywords);

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

    // ── Discovery ───────────────────────────────────────────────────────

    [Event(EvtDiscoveryStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Looking up Hue bridges")]
    public void DiscoveryStarted()
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryStarted);
    }

    [Event(EvtDiscoveryStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "discover start | source=cloud | url={0}")]
    public void DiscoveryStartedDetail(string url)
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryStartedDetail, url);
    }

    [Event(EvtDiscoveryFound,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Found Hue bridges")]
    public void DiscoveryFound()
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryFound);
    }

    [Event(EvtDiscoveryFoundDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "discover result | count={0}")]
    public void DiscoveryFoundDetail(int count)
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryFoundDetail, count);
    }

    [Event(EvtDiscoveryBridgeFound,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "discover result | bridge_id={0} | bridge_ip={1}")]
    public void DiscoveryBridgeFound(string bridge_id, string bridge_ip)
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryBridgeFound, bridge_id, bridge_ip);
    }

    [Event(EvtDiscoveryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Cloud discovery failed")]
    public void DiscoveryFailed()
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryFailed);
    }

    [Event(EvtDiscoveryFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "discover failed | ex_type={0} | message={1}")]
    public void DiscoveryFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryFailedDetail, ex_type, message);
    }

    [Event(EvtLocalDiscoveryStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Looking for Hue bridges on the local network")]
    public void LocalDiscoveryStarted()
    {
        if (IsEnabled()) WriteEvent(EvtLocalDiscoveryStarted);
    }

    [Event(EvtLocalDiscoveryStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "discover start | source=mdns | service={0}")]
    public void LocalDiscoveryStartedDetail(string service)
    {
        if (IsEnabled()) WriteEvent(EvtLocalDiscoveryStartedDetail, service);
    }

    [Event(EvtLocalDiscoveryCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Local Hue bridge discovery completed")]
    public void LocalDiscoveryCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtLocalDiscoveryCompleted);
    }

    [Event(EvtLocalDiscoveryCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "discover result | source=mdns | count={0}")]
    public void LocalDiscoveryCompletedDetail(int count)
    {
        if (IsEnabled()) WriteEvent(EvtLocalDiscoveryCompletedDetail, count);
    }

    [Event(EvtLocalDiscoveryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Local Hue bridge discovery failed")]
    public void LocalDiscoveryFailed()
    {
        if (IsEnabled()) WriteEvent(EvtLocalDiscoveryFailed);
    }

    [Event(EvtLocalDiscoveryFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "discover failed | source=mdns | ex_type={0} | message={1}")]
    public void LocalDiscoveryFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtLocalDiscoveryFailedDetail, ex_type, message);
    }

    // ── Pairing ─────────────────────────────────────────────────────────

    [Event(EvtPairingStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing started — press the link button on the bridge")]
    public void PairingStarted()
    {
        if (IsEnabled()) WriteEvent(EvtPairingStarted);
    }

    [Event(EvtPairingStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair start | bridge_ip={0} | timeout_sec={1} | devicetype={2}")]
    public void PairingStartedDetail(string bridge_ip, int timeout_sec, string devicetype)
    {
        if (IsEnabled()) WriteEvent(EvtPairingStartedDetail, bridge_ip, timeout_sec, devicetype);
    }

    [Event(EvtBridgePaired,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge paired")]
    public void BridgePaired()
    {
        if (IsEnabled()) WriteEvent(EvtBridgePaired);
    }

    // A …Detail already exists (the pair-result mirror with username), so this
    // milestone mirror is named …Detail2 per the Verbose/Info separation rule.
    [Event(EvtBridgePairedDetail2,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair milestone | bridge_id={0}")]
    public void BridgePairedDetail2(string bridge_id)
    {
        if (IsEnabled()) WriteEvent(EvtBridgePairedDetail2, bridge_id);
    }

    [Event(EvtBridgePairedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair result | bridge_id={0} | username={1} | clientkey=[redacted]")]
    public void BridgePairedDetail(string bridge_id, string username_head)
    {
        if (IsEnabled()) WriteEvent(EvtBridgePairedDetail, bridge_id, username_head);
    }

    [Event(EvtPairingWaiting,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair waiting | error_type=101 | next_attempt_in_ms={0}")]
    public void PairingWaiting(int next_attempt_in_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPairingWaiting, next_attempt_in_ms);
    }

    [Event(EvtPairingRejected,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing rejected by the bridge")]
    public void PairingRejected()
    {
        if (IsEnabled()) WriteEvent(EvtPairingRejected);
    }

    [Event(EvtPairingRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair rejected | error_type={0} | description={1}")]
    public void PairingRejectedDetail(int error_type, string description)
    {
        if (IsEnabled()) WriteEvent(EvtPairingRejectedDetail, error_type, description);
    }

    [Event(EvtPairingTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing timed out — the link button was not pressed in time")]
    public void PairingTimedOut()
    {
        if (IsEnabled()) WriteEvent(EvtPairingTimedOut);
    }

    [Event(EvtBridgeUnreachable,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Bridge unreachable during pairing")]
    public void BridgeUnreachable()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeUnreachable);
    }

    [Event(EvtBridgeUnreachableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge unreachable | ex_type={0} | message={1}")]
    public void BridgeUnreachableDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeUnreachableDetail, ex_type, message);
    }

    [Event(EvtPairingHttpError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing HTTP request failed")]
    public void PairingHttpError()
    {
        if (IsEnabled()) WriteEvent(EvtPairingHttpError);
    }

    [Event(EvtPairingHttpErrorDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pair http error | http_status={0} | reason={1}")]
    public void PairingHttpErrorDetail(int http_status, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtPairingHttpErrorDetail, http_status, reason);
    }

    // ── Groups ──────────────────────────────────────────────────────────

    [Event(EvtListingGroups,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Listing groups")]
    public void ListingGroups()
    {
        if (IsEnabled()) WriteEvent(EvtListingGroups);
    }

    [Event(EvtBridgeReturnedNoGroups,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Bridge returned no groups payload")]
    public void BridgeReturnedNoGroups()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeReturnedNoGroups);
    }

    [Event(EvtGroupsListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "groups list | bridge_id={0} | count={1}")]
    public void GroupsListed(string bridge_id, int count)
    {
        if (IsEnabled()) WriteEvent(EvtGroupsListed, bridge_id, count);
    }

    [Event(EvtGroupListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "group | id={0} | name={1} | type={2} | lights={3}")]
    public void GroupListed(string id, string name, string type, int lights)
    {
        if (IsEnabled()) WriteEvent(EvtGroupListed, id, name, type, lights);
    }

    // ── Color push ──────────────────────────────────────────────────────

    [Event(EvtSetColorFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Setting a colour failed")]
    public void SetColorFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSetColorFailed);
    }

    [Event(EvtSetColorFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "set colour failed | target={0} | http_status={1}")]
    public void SetColorFailedDetail(string target, int http_status)
    {
        if (IsEnabled()) WriteEvent(EvtSetColorFailedDetail, target, http_status);
    }

    [Event(EvtPushColorOff,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "push colour | {0} | rgb={1},{2},{3} | on=false | tt_ds={4}")]
    public void PushColorOff(string target, int r, int g, int b, int tt_ds)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushColorOff, target, r, g, b, tt_ds);
    }

    [Event(EvtPushColor,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "push colour | {0} | rgb={1},{2},{3} | xy={4:F4},{5:F4} | bri={6} | tt_ds={7}")]
    public void PushColor(string target, int r, int g, int b, double xy_x, double xy_y, int bri, int tt_ds)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushColor, target, r, g, b, xy_x, xy_y, bri, tt_ds);
    }

    // ── Entertainment ───────────────────────────────────────────────────

    [Event(EvtListingEntertainmentConfigs,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Listing entertainment configurations")]
    public void ListingEntertainmentConfigs()
    {
        if (IsEnabled()) WriteEvent(EvtListingEntertainmentConfigs);
    }

    [Event(EvtEntertainmentEmpty,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment list | count=0")]
    public void EntertainmentEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentEmpty);
    }

    [Event(EvtEntertainmentV2Catalog,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment v2 catalog | services={0} | lights={1}")]
    public void EntertainmentV2Catalog(int services, int lights)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentV2Catalog, services, lights);
    }

    [Event(EvtEntertainmentListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment list | count={0}")]
    public void EntertainmentListed(int count)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentListed, count);
    }

    [Event(EvtEntertainmentArea,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment | id={0} | name={1} | lights={2}")]
    public void EntertainmentArea(string id, string name, int lights_count)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentArea, id, name, lights_count);
    }

    [Event(EvtPlacementListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "placement | ent_id={0} | light_id={1} | name={2} | x={3:F3} | y={4:F3} | z={5:F3}")]
    public void PlacementListed(string ent_id, string light_id, string name, double x, double y, double z)
    {
        if (IsEnabled()) WriteEvent(EvtPlacementListed, ent_id, light_id, name, x, y, z);
    }

    [Event(EvtClipV2GetFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A CLIP v2 request failed")]
    public void ClipV2GetFailed()
    {
        if (IsEnabled()) WriteEvent(EvtClipV2GetFailed);
    }

    [Event(EvtClipV2GetFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "clip v2 get failed | path={0} | http_status={1}")]
    public void ClipV2GetFailedDetail(string path, int http_status)
    {
        if (IsEnabled()) WriteEvent(EvtClipV2GetFailedDetail, path, http_status);
    }

    [Event(EvtEntertainmentRestFallback,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Hue Entertainment unavailable — using REST fallback")]
    public void EntertainmentRestFallback()
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentRestFallback);
    }

    [Event(EvtEntertainmentRestFallbackDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment fallback | reason={0} | ex_type={1} | message={2}")]
    public void EntertainmentRestFallbackDetail(string reason, string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentRestFallbackDetail, reason, ex_type, message);
    }

    [Event(EvtEntertainmentPrePrimeStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Preparing Hue Entertainment lights")]
    public void EntertainmentPrePrimeStarting()
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentPrePrimeStarting);
    }

    [Event(EvtEntertainmentPrePrimeDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "entertainment pre-prime | ent_id={0} | lights={1} | rgb=1,1,1")]
    public void EntertainmentPrePrimeDetail(string ent_id, int lights)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtEntertainmentPrePrimeDetail, ent_id, lights);
    }

    [Event(EvtEntertainmentPrePrimeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Preparing Hue Entertainment lights failed")]
    public void EntertainmentPrePrimeFailed()
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentPrePrimeFailed);
    }

    [Event(EvtEntertainmentPrePrimeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment pre-prime failed | ex_type={0} | message={1}")]
    public void EntertainmentPrePrimeFailedDetail(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentPrePrimeFailedDetail, ex_type, message);
    }

    [Event(EvtEntertainmentStreamingStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Starting Hue Entertainment streaming")]
    public void EntertainmentStreamingStarting()
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentStreamingStarting);
    }

    [Event(EvtEntertainmentStreamingStartingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment start | ent_id={0} | name={1} | channels={2}")]
    public void EntertainmentStreamingStartingDetail(string ent_id, string name, int channels)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentStreamingStartingDetail, ent_id, name, channels);
    }

    [Event(EvtEntertainmentTransportConnecting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Connecting Hue Entertainment transport")]
    public void EntertainmentTransportConnecting()
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentTransportConnecting);
    }

    [Event(EvtEntertainmentTransportConnected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Hue Entertainment transport connected")]
    public void EntertainmentTransportConnected()
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentTransportConnected);
    }

    [Event(EvtEntertainmentStreamPrimed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "entertainment primed | ent_id={0} | channels={1} | rgb=0,0,0")]
    public void EntertainmentStreamPrimed(string ent_id, int channels)
    {
        if (!IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtEntertainmentStreamPrimed, ent_id, channels);
    }

    // ── Identify ────────────────────────────────────────────────────────

    [Event(EvtIdentifyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Identifying a light failed")]
    public void IdentifyFailed()
    {
        if (IsEnabled()) WriteEvent(EvtIdentifyFailed);
    }

    [Event(EvtIdentifyFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "identify failed | phase={0} | light_id={1} | http_status={2}")]
    public void IdentifyFailedDetail(string phase, string light_id, int http_status)
    {
        if (IsEnabled()) WriteEvent(EvtIdentifyFailedDetail, phase, light_id, http_status);
    }

    [Event(EvtLightIdentified,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "light identify | light_id={0} | alert={1} | phase={2}")]
    public void LightIdentified(string light_id, string alert, string phase)
    {
        if (IsEnabled()) WriteEvent(EvtLightIdentified, light_id, alert, phase);
    }

    // ── Lights listing ──────────────────────────────────────────────────

    [Event(EvtListingLightsInGroup,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Listing lights in a group")]
    public void ListingLightsInGroup()
    {
        if (IsEnabled()) WriteEvent(EvtListingLightsInGroup);
    }

    [Event(EvtListingLightsInGroupDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "listing lights | group_id={0}")]
    public void ListingLightsInGroupDetail(string group_id)
    {
        if (IsEnabled()) WriteEvent(EvtListingLightsInGroupDetail, group_id);
    }

    [Event(EvtLightsListedEmpty,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "lights list | group_id={0} | count=0")]
    public void LightsListedEmpty(string group_id)
    {
        if (IsEnabled()) WriteEvent(EvtLightsListedEmpty, group_id);
    }

    [Event(EvtBridgeReturnedNoLights,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Bridge returned no lights")]
    public void BridgeReturnedNoLights()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeReturnedNoLights);
    }

    [Event(EvtBridgeReturnedNoLightsDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "no lights payload | group_id={0}")]
    public void BridgeReturnedNoLightsDetail(string group_id)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeReturnedNoLightsDetail, group_id);
    }

    [Event(EvtLightsListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "lights list | group_id={0} | count={1}")]
    public void LightsListed(string group_id, int count)
    {
        if (IsEnabled()) WriteEvent(EvtLightsListed, group_id, count);
    }

    [Event(EvtLightListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "light | id={0} | name={1} | type={2} | reachable={3}")]
    public void LightListed(string id, string name, string type, bool reachable)
    {
        if (IsEnabled()) WriteEvent(EvtLightListed, id, name, type, reachable);
    }

    // ── v2 id maps + EventStream ────────────────────────────────────────

    [Event(EvtFetchingV2IdMaps,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Fetching CLIP v2 id maps (light + grouped_light)")]
    public void FetchingV2IdMaps()
    {
        if (IsEnabled()) WriteEvent(EvtFetchingV2IdMaps);
    }

    [Event(EvtV2IdMapsFetched,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "v2 id maps | lights={0} | grouped_lights={1}")]
    public void V2IdMapsFetched(int lights, int grouped_lights)
    {
        if (IsEnabled()) WriteEvent(EvtV2IdMapsFetched, lights, grouped_lights);
    }

    [Event(EvtEventStreamStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "Hue EventStream subscriber starting")]
    public void EventStreamStarting()
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamStarting);
    }

    [Event(EvtEventStreamReconnecting,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "EventStream reconnect — reason={0}")]
    public void EventStreamReconnecting(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamReconnecting, reason);
    }

    [Event(EvtEventStreamStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "Hue EventStream subscriber stopped")]
    public void EventStreamStopped()
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamStopped);
    }
}

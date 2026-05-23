using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

// Lighting module provider. Couvre la découverte bridge Hue (cloud
// endpoint), le pairing CLIP v1 (link-button), la liste des groupes,
// la liste des entertainment configurations (CLIP v2), la liste des
// lights par groupe, l'envoi de couleurs (REST PUT /state ou /action),
// et les opérations d'identification visuelle (alert flash).
//
// Le module abstrait à terme plusieurs drivers (WLED, DMX, HomeAssist).
// Pour V0 seul Hue est implémenté ; le provider est unique pour le
// module entier, les futurs drivers ajouteront leurs events sous le
// même provider plutôt que de créer un Deckle.Lighting.* enfant.
[EventSource(Name = "Deckle.Lighting")]
public sealed class DeckleLightingSource : DeckleEventSource
{
    public static readonly DeckleLightingSource Log = new();

    private DeckleLightingSource() { }

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
           Message = "Found {0} Hue bridges")]
    public void DiscoveryFound(int count)
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryFound, count);
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
           Message = "Cloud discovery failed — {0}: {1}")]
    public void DiscoveryFailed(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryFailed, ex_type, message);
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
           Message = "Bridge paired ({0})")]
    public void BridgePaired(string bridge_id)
    {
        if (IsEnabled()) WriteEvent(EvtBridgePaired, bridge_id);
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
           Message = "Pairing rejected by bridge — type={0}: {1}")]
    public void PairingRejected(int error_type, string description)
    {
        if (IsEnabled()) WriteEvent(EvtPairingRejected, error_type, description);
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
           Message = "Bridge unreachable during pairing — {0}: {1}")]
    public void BridgeUnreachable(string ex_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeUnreachable, ex_type, message);
    }

    [Event(EvtPairingHttpError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pairing HTTP error | hr={0} | reason={1}")]
    public void PairingHttpError(int http_status, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtPairingHttpError, http_status, reason);
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
           Message = "Set colour failed | {0} | hr={1}")]
    public void SetColorFailed(string target, int http_status)
    {
        if (IsEnabled()) WriteEvent(EvtSetColorFailed, target, http_status);
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
           Message = "CLIP v2 GET failed | path={0} | hr={1}")]
    public void ClipV2GetFailed(string path, int http_status)
    {
        if (IsEnabled()) WriteEvent(EvtClipV2GetFailed, path, http_status);
    }

    // ── Identify ────────────────────────────────────────────────────────

    [Event(EvtIdentifyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Identify {0} failed | light_id={1} | hr={2}")]
    public void IdentifyFailed(string phase, string light_id, int http_status)
    {
        if (IsEnabled()) WriteEvent(EvtIdentifyFailed, phase, light_id, http_status);
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
           Message = "Listing lights in group {0}")]
    public void ListingLightsInGroup(string group_id)
    {
        if (IsEnabled()) WriteEvent(EvtListingLightsInGroup, group_id);
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
           Message = "Bridge returned no lights payload | group_id={0}")]
    public void BridgeReturnedNoLights(string group_id)
    {
        if (IsEnabled()) WriteEvent(EvtBridgeReturnedNoLights, group_id);
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

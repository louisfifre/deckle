using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

public sealed partial class DeckleLightingSource
{
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
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "event stream subscriber starting")]
    public void EventStreamStarting()
    {
        if (!IsEventStreamDetailEnabled()) return;
        WriteEvent(EvtEventStreamStarting);
    }

    [Event(EvtEventStreamReconnecting,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "EventStream reconnect — reason={0}")]
    public void EventStreamReconnecting(string reason)
    {
        if (!IsEventStreamDetailEnabled()) return;
        WriteEvent(EvtEventStreamReconnecting, reason);
    }

    [Event(EvtEventStreamStopped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "event stream subscriber stopped")]
    public void EventStreamStopped()
    {
        if (!IsEventStreamDetailEnabled()) return;
        WriteEvent(EvtEventStreamStopped);
    }

    [Event(EvtEventStreamIncident,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Hue event monitoring unavailable — external changes may be overwritten")]
    public void EventStreamIncident()
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamIncident);
    }

    [Event(EvtEventStreamIncidentDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "event stream unavailable | duration_ms={0} | failures={1} | reason={2} | ex_type={3} | ex_message={4}")]
    public void EventStreamIncidentDetail(
        long duration_ms,
        int failures,
        string reason,
        string ex_type,
        string ex_message)
    {
        if (!IsEventStreamDetailEnabled()) return;
        WriteEvent(
            EvtEventStreamIncidentDetail,
            duration_ms,
            failures,
            reason,
            ex_type,
            ex_message);
    }

    [Event(EvtEventStreamRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Hue event monitoring restored")]
    public void EventStreamRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamRecovered);
    }

    [Event(EvtEventStreamRecoveryDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "event stream restored | duration_ms={0} | failures={1}")]
    public void EventStreamRecoveryDetail(long duration_ms, int failures)
    {
        if (!IsEventStreamDetailEnabled()) return;
        WriteEvent(EvtEventStreamRecoveryDetail, duration_ms, failures);
    }

    [NonEvent]
    internal bool IsEventStreamDetailEnabled()
        => OperationalLogAdmission.IsScopedDetailEnabled(
            OperationalLogActivity.Ambient,
            this,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Pipeline);
}

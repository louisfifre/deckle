using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

public sealed partial class DeckleLightingSource
{
    // ── Identify ────────────────────────────────────────────────────────

    [Event(EvtIdentifyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Identifying a light failed | phase={0} | light_id={1} | status={2}")]
    public void IdentifyFailed(string phase, string light_id, int status)
    {
        if (IsEnabled()) WriteEvent(EvtIdentifyFailed, phase, light_id, status);
    }

    [Event(EvtLightIdentified,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "light identify | light_id={0} | alert={1} | phase={2}")]
    public void LightIdentified(string light_id, string alert, string phase)
    {
        if (!IsHueDetailEnabled(Keywords.Push)) return;
        WriteEvent(EvtLightIdentified, light_id, alert, phase);
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
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtListingLightsInGroupDetail, group_id);
    }

    [Event(EvtLightsListedEmpty,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "lights list | group_id={0} | count=0")]
    public void LightsListedEmpty(string group_id)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtLightsListedEmpty, group_id);
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
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtBridgeReturnedNoLightsDetail, group_id);
    }

    [Event(EvtLightsListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "lights list | group_id={0} | count={1}")]
    public void LightsListed(string group_id, int count)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtLightsListed, group_id, count);
    }

    [Event(EvtLightListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "light | id={0} | name={1} | type={2} | reachable={3}")]
    public void LightListed(string id, string name, string type, bool reachable)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtLightListed, id, name, type, reachable);
    }

    // ── v2 id maps + EventStream ────────────────────────────────────────

    [Event(EvtFetchingV2IdMaps,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "fetching v2 id maps | resources=light,grouped_light")]
    public void FetchingV2IdMaps()
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtFetchingV2IdMaps);
    }

    [Event(EvtV2IdMapsFetched,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "v2 id maps | lights={0} | grouped_lights={1}")]
    public void V2IdMapsFetched(int lights, int grouped_lights)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtV2IdMapsFetched, lights, grouped_lights);
    }

    [Event(EvtEventStreamStarting,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "event stream subscriber starting")]
    public void EventStreamStarting()
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline | Keywords.Lifecycle)) return;
        WriteEvent(EvtEventStreamStarting);
    }

    // One per reconnect attempt; the incident Warning below opens only
    // once the outage outlasts HueEventStreamEpisode.IncidentDelay.
    [Event(EvtEventStreamReconnecting,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "event stream reconnect | reason={0}")]
    public void EventStreamReconnecting(string reason)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtEventStreamReconnecting, reason);
    }

    [Event(EvtEventStreamStopped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "event stream subscriber stopped | outcome={0} | duration_ms={1} | ex_type={2} | ex_message={3}")]
    public void EventStreamStopped(string outcome, long duration_ms, string ex_type, string ex_message)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline | Keywords.Lifecycle)) return;
        WriteEvent(EvtEventStreamStopped, outcome, duration_ms, ex_type, ex_message);
    }

    [Event(EvtEventStreamIncident,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Hue event monitoring is unavailable, external changes may be overwritten | duration_ms={0} | failures={1} | reason={2} | ex_type={3} | ex_message={4}")]
    public void EventStreamIncident(
        long duration_ms,
        int failures,
        string reason,
        string ex_type,
        string ex_message)
    {
        if (!IsEnabled()) return;
        WriteEvent(
            EvtEventStreamIncident,
            duration_ms,
            failures,
            reason,
            ex_type,
            ex_message);
    }

    [Event(EvtEventStreamRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Journal,
           Message = "Hue event monitoring restored")]
    public void EventStreamRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamRecovered);
    }

    // The other close of EventStreamIncident: the subscriber was stopped while
    // the incident was still open, so no reconnection will ever restore it.
    // Without this line the journal would hold a Warning that never ends.
    [Event(EvtEventStreamIncidentAbandoned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Journal,
           Message = "Hue event monitoring stopped while it was unavailable | duration_ms={0} | failures={1}")]
    public void EventStreamIncidentAbandoned(long duration_ms, int failures)
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamIncidentAbandoned, duration_ms, failures);
    }

    [Event(EvtEventStreamRecoveryDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "event stream restored | duration_ms={0} | failures={1}")]
    public void EventStreamRecoveryDetail(long duration_ms, int failures)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtEventStreamRecoveryDetail, duration_ms, failures);
    }
}

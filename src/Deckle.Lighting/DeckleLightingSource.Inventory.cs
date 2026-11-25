using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

public sealed partial class DeckleLightingSource
{
    // ── Identify ────────────────────────────────────────────────────────

    [Event(EvtIdentifyFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Identifying a light failed, so it did not blink\nphase={0} | light_id={1} | status={2}")]
    public void IdentifyFailed(string phase, string light_id, int status)
    {
        if (IsEnabled()) WriteEvent(EvtIdentifyFailed, phase, light_id, status);
    }

    [Event(EvtLightIdentified,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "light_id={0} | alert={1} | phase={2}")]
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
           Message = "group_id={0}")]
    public void ListingLightsInGroupDetail(string group_id)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtListingLightsInGroupDetail, group_id);
    }

    [Event(EvtLightsListedEmpty,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "group_id={0}")]
    public void LightsListedEmpty(string group_id)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtLightsListedEmpty, group_id);
    }

    [Event(EvtBridgeReturnedNoLights,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The Hue bridge returned no lights")]
    public void BridgeReturnedNoLights()
    {
        if (IsEnabled()) WriteEvent(EvtBridgeReturnedNoLights);
    }

    [Event(EvtBridgeReturnedNoLightsDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "group_id={0}")]
    public void BridgeReturnedNoLightsDetail(string group_id)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtBridgeReturnedNoLightsDetail, group_id);
    }

    [Event(EvtLightsListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "group_id={0} | count={1}")]
    public void LightsListed(string group_id, int count)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtLightsListed, group_id, count);
    }

    [Event(EvtLightListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "id={0} | name={1} | type={2} | reachable={3}")]
    public void LightListed(string id, string name, string type, bool reachable)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtLightListed, id, name, type, reachable);
    }
}

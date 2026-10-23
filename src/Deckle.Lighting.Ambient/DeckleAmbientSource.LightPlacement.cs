using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // ── Per-light placement (LightPlacementResolver + LightZonesCard) ───

    [Event(EvtLightCallFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A call to the Hue bridge for the light list failed\nop={0} | ex_type={1} | ex_message={2}")]
    public void LightCallFailed(string op, string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Push))
            WriteEvent(EvtLightCallFailed, op, ex_type, ex_message);
    }

    [Event(EvtLightsResolved,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "group_id={0} | group_name={1} | from_group={2}")]
    public void LightsResolved(string group_id, string group_name, int from_group)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtLightsResolved, group_id, group_name, from_group);
    }

    [Event(EvtEntertainmentAreaUsed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Using an entertainment area as the lights source")]
    public void EntertainmentAreaUsed()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtEntertainmentAreaUsed);
    }

    [Event(EvtEntertainmentAreaUsedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "name={0} | lights={1}")]
    public void EntertainmentAreaUsedDetail(string name, int lights)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtEntertainmentAreaUsedDetail, name, lights);
    }

    [Event(EvtEntertainmentAreaMatched,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "result={0} | ent_id={1} | name={2} | overlap={3}")]
    public void EntertainmentAreaMatched(string result, string ent_id, string name, int overlap)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtEntertainmentAreaMatched, result, ent_id, name, overlap);
    }

    [Event(EvtZoneSuggested,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "id={0} | zone={1} | ent_name={2} | x={3:F2} | y={4:F2} | z={5:F2}")]
    public void ZoneSuggested(string id, string zone, string ent_name, double x, double y, double z)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtZoneSuggested, id, zone, ent_name, x, y, z);
    }

    [Event(EvtLightZoneUpdated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A light zone was updated")]
    public void LightZoneUpdated()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtLightZoneUpdated);
    }

    [Event(EvtLightZoneUpdatedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "id={0} | zone={1}")]
    public void LightZoneUpdatedDetail(string id, string zone)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtLightZoneUpdatedDetail, id, zone);
    }

    [Event(EvtLightBrightnessChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "id={0} | value={1:F2}")]
    public void LightBrightnessChanged(string id, double value)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtLightBrightnessChanged, id, value);
    }
}

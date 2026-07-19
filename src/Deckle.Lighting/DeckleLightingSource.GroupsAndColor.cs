using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

public sealed partial class DeckleLightingSource
{
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
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Setting a colour failed")]
    public void SetColorFailed()
    {
        if (!OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtSetColorFailed);
    }

    [Event(EvtSetColorFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "set colour failed | target={0} | http_status={1}")]
    public void SetColorFailedDetail(string target, int http_status)
    {
        if (!OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtSetColorFailedDetail, target, http_status);
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

}

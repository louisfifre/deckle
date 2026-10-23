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
           Message = "bridge_id={0} | count={1}")]
    public void GroupsListed(string bridge_id, int count)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtGroupsListed, bridge_id, count);
    }

    [Event(EvtGroupListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "id={0} | name={1} | type={2} | lights={3}")]
    public void GroupListed(string id, string name, string type, int lights)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtGroupListed, id, name, type, lights);
    }

    // ── Color push ──────────────────────────────────────────────────────

    // Per push, so Verbose: the Ambient push-health episode owns the
    // Warning for repeated failures.
    [Event(EvtSetColorFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "target={0} | status={1}")]
    public void SetColorFailed(string target, int status)
    {
        if (!IsHueDetailEnabled(Keywords.Push)) return;
        WriteEvent(EvtSetColorFailed, target, status);
    }

    [Event(EvtPushColorOff,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "target={0} | r={1} | g={2} | b={3} | tt_ds={4}")]
    public void PushColorOff(string target, int r, int g, int b, int tt_ds)
    {
        if (!IsHueDetailEnabled(Keywords.Push)) return;
        WriteEvent(EvtPushColorOff, target, r, g, b, tt_ds);
    }

    [Event(EvtPushColor,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "target={0} | r={1} | g={2} | b={3} | xy_x={4:F4} | xy_y={5:F4} | bri={6} | tt_ds={7}")]
    public void PushColor(string target, int r, int g, int b, double xy_x, double xy_y, int bri, int tt_ds)
    {
        if (!IsHueDetailEnabled(Keywords.Push)) return;
        WriteEvent(EvtPushColor, target, r, g, b, xy_x, xy_y, bri, tt_ds);
    }
}

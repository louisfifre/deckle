using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

public sealed partial class DeckleLightingSource
{
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
           Message = "url={0}")]
    public void DiscoveryStartedDetail(string url)
    {
        if (!IsHueDetailEnabled(Keywords.Lifecycle)) return;
        WriteEvent(EvtDiscoveryStartedDetail, url);
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
           Message = "count={0}")]
    public void DiscoveryFoundDetail(int count)
    {
        if (!IsHueDetailEnabled(Keywords.Lifecycle)) return;
        WriteEvent(EvtDiscoveryFoundDetail, count);
    }

    [Event(EvtDiscoveryBridgeFound,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "bridge_id={0} | bridge_ip={1}")]
    public void DiscoveryBridgeFound(string bridge_id, string bridge_ip)
    {
        if (!IsHueDetailEnabled(Keywords.Lifecycle)) return;
        WriteEvent(EvtDiscoveryBridgeFound, bridge_id, bridge_ip);
    }

    [Event(EvtDiscoveryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Finding the Hue bridge through the cloud failed\nex_type={0} | ex_message={1}")]
    public void DiscoveryFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtDiscoveryFailed, ex_type, ex_message);
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
           Message = "service={0}")]
    public void LocalDiscoveryStartedDetail(string service)
    {
        if (!IsHueDetailEnabled(Keywords.Lifecycle)) return;
        WriteEvent(EvtLocalDiscoveryStartedDetail, service);
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
           Message = "count={0}")]
    public void LocalDiscoveryCompletedDetail(int count)
    {
        if (!IsHueDetailEnabled(Keywords.Lifecycle)) return;
        WriteEvent(EvtLocalDiscoveryCompletedDetail, count);
    }

    [Event(EvtLocalDiscoveryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Local Hue bridge discovery failed\nex_type={0} | ex_message={1} | win32_error={2}")]
    public void LocalDiscoveryFailed(string ex_type, string ex_message, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtLocalDiscoveryFailed, ex_type, ex_message, win32_error);
    }
}

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

}

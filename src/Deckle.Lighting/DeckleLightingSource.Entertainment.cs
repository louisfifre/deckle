using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting;

public sealed partial class DeckleLightingSource
{
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

}

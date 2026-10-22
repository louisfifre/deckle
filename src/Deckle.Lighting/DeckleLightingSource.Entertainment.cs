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
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtEntertainmentEmpty);
    }

    [Event(EvtEntertainmentV2Catalog,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment v2 catalog | services={0} | lights={1}")]
    public void EntertainmentV2Catalog(int services, int lights)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtEntertainmentV2Catalog, services, lights);
    }

    [Event(EvtEntertainmentListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment list | count={0}")]
    public void EntertainmentListed(int count)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtEntertainmentListed, count);
    }

    [Event(EvtEntertainmentArea,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entertainment | id={0} | name={1} | lights={2}")]
    public void EntertainmentArea(string id, string name, int lights_count)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtEntertainmentArea, id, name, lights_count);
    }

    [Event(EvtPlacementListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "placement | ent_id={0} | light_id={1} | name={2} | x={3:F3} | y={4:F3} | z={5:F3}")]
    public void PlacementListed(string ent_id, string light_id, string name, double x, double y, double z)
    {
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtPlacementListed, ent_id, light_id, name, x, y, z);
    }

    // status=200 with a description is a CLIP v2 error carried in the body
    // of a successful HTTP response.
    [Event(EvtClipV2RequestFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A CLIP v2 request failed | method={0} | path={1} | status={2} | description={3}")]
    public void ClipV2RequestFailed(string method, string path, int status, string description)
    {
        if (IsEnabled()) WriteEvent(EvtClipV2RequestFailed, method, path, status, description);
    }

    [Event(EvtLightOutputSelected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Push),
           Tags = ObservationTags.Journal,
           Message = "Hue output selected | output={0} | reason={1}")]
    public void LightOutputSelected(string output, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtLightOutputSelected, output, reason);
    }

    [Event(EvtEntertainmentRestFallback,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Hue Entertainment is unavailable, using the REST fallback | reason={0} | ex_type={1} | ex_message={2}")]
    public void EntertainmentRestFallback(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentRestFallback, reason, ex_type, ex_message);
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
        if (!IsHueDetailEnabled(Keywords.Push)) return;
        WriteEvent(EvtEntertainmentPrePrimeDetail, ent_id, lights);
    }

    [Event(EvtEntertainmentPrePrimeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Preparing Hue Entertainment lights failed | ex_type={0} | ex_message={1}")]
    public void EntertainmentPrePrimeFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentPrePrimeFailed, ex_type, ex_message);
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
        if (!IsHueDetailEnabled(Keywords.Pipeline)) return;
        WriteEvent(EvtEntertainmentStreamingStartingDetail, ent_id, name, channels);
    }

    [Event(EvtEntertainmentTransportConnecting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Connecting Hue Entertainment transport")]
    public void EntertainmentTransportConnecting()
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentTransportConnecting);
    }

    // The session's start milestone: the DTLS handshake succeeded and the
    // first frame went out. EntertainmentSessionClosed is its end.
    [Event(EvtEntertainmentTransportConnected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Hue Entertainment session opened | elapsed_ms={0}")]
    public void EntertainmentTransportConnected(long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentTransportConnected, elapsed_ms);
    }

    [Event(EvtEntertainmentConnectFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Hue Entertainment session could not open | stage={0} | ex_type={1} | ex_message={2} | elapsed_ms={3}")]
    public void EntertainmentConnectFailed(string stage, string ex_type, string ex_message, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentConnectFailed, stage, ex_type, ex_message, elapsed_ms);
    }

    [Event(EvtEntertainmentConnectCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "Hue Entertainment session opening cancelled | stage={0} | elapsed_ms={1}")]
    public void EntertainmentConnectCancelled(string stage, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentConnectCancelled, stage, elapsed_ms);
    }

    [Event(EvtEntertainmentSessionClosed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Tags = ObservationTags.Journal,
           Message = "Hue Entertainment session closed | outcome={0} | duration_ms={1} | frames_sent={2} | send_failures={3}")]
    public void EntertainmentSessionClosed(string outcome, long duration_ms, long frames_sent, int send_failures)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentSessionClosed, outcome, duration_ms, frames_sent, send_failures);
    }

    // A missed action=stop leaves the area in streaming mode on the bridge
    // until its own timeout, so the Hue app shows it busy meanwhile.
    [Event(EvtEntertainmentStreamingStopFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Resource),
           Message = "Hue Entertainment streaming could not be stopped | ex_type={0} | ex_message={1}")]
    public void EntertainmentStreamingStopFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtEntertainmentStreamingStopFailed, ex_type, ex_message);
    }

    [Event(EvtEntertainmentStreamPrimed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "entertainment primed | ent_id={0} | channels={1} | rgb=0,0,0")]
    public void EntertainmentStreamPrimed(string ent_id, int channels)
    {
        if (!IsHueDetailEnabled(Keywords.Push)) return;
        WriteEvent(EvtEntertainmentStreamPrimed, ent_id, channels);
    }
}

using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // ── Push ticks ──────────────────────────────────────────────────────

    [Event(EvtPushGroup,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "push | mode=group | rgb={0},{1},{2} | off={3} | push_ms={4:F1}")]
    public void PushGroup(int r, int g, int b, bool off, double push_ms)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushGroup, r, g, b, off, push_ms);
    }

    [Event(EvtPushMulti,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "push | mode=multi | lights={0}/{1} | colors={2} | push_ms={3:F1}")]
    public void PushMulti(int pushed_lights, int total_lights, string colors, double push_ms)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushMulti, pushed_lights, total_lights, colors, push_ms);
    }

    // One raw attempt per failed push; the incident milestone opens past the
    // threshold. retry_delay_ms is the back-off the loop applies next.
    [Event(EvtPushAttemptFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "push attempt failed | failures={0} | retry_delay_ms={1} | ex_type={2} | ex_message={3}")]
    public void PushAttemptFailedDetail(int failures, int retry_delay_ms, string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushAttemptFailedDetail, failures, retry_delay_ms, ex_type, ex_message);
    }

    [Event(EvtPushIncidentOpened,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Hue bridge pushes are failing, Ambient lighting is retrying | failures={0} | ex_type={1} | ex_message={2}")]
    public void PushIncidentOpened(int failures, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPushIncidentOpened, failures, ex_type, ex_message);
    }

    [Event(EvtPushRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "Hue bridge pushes recovered")]
    public void PushRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtPushRecovered);
    }

    // status = the HTTP status of the rejection, 0 when the cause is not an
    // HTTP response (a driver InvalidOperationException).
    [Event(EvtPushRejected,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Hue rejected Ambient lighting, the pipeline is stopping | status={0} | ex_type={1} | ex_message={2}")]
    public void PushRejected(int status, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPushRejected, status, ex_type, ex_message);
    }

    [Event(EvtPushEpisodeDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "push episode | outcome={0} | failures={1} | duration_ms={2} | ex_type={3} | ex_message={4}")]
    public void PushEpisodeDetail(
        string outcome, int failures, long duration_ms, string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushEpisodeDetail, outcome, failures, duration_ms, ex_type, ex_message);
    }

    // push_* stats cover the pushes of the period; they are 0 when no push ran.
    [Event(EvtHeartbeat,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "heartbeat | mode={0} | period_s={1:F1} | target_hz={2} | effective_hz={3:F1} | ticks={4} | pushed={5} | dropped={6} | skipped_slots={7} | unmapped_lights={8} | push_failures={9} | push_avg_ms={10:F1} | push_p95_ms={11:F1} | push_max_ms={12:F1}")]
    public void Heartbeat(
        string mode,
        double period_s,
        int target_hz,
        double effective_hz,
        int ticks,
        int pushed,
        int dropped,
        long skipped_slots,
        int unmapped_lights,
        int push_failures,
        double push_avg_ms,
        double push_p95_ms,
        double push_max_ms)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(
            EvtHeartbeat,
            mode,
            period_s,
            target_hz,
            effective_hz,
            ticks,
            pushed,
            dropped,
            skipped_slots,
            unmapped_lights,
            push_failures,
            push_avg_ms,
            push_p95_ms,
            push_max_ms);
    }
}

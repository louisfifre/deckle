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

    [Event(EvtPushGroupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Group push failed")]
    public void PushGroupFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPushGroupFailed);
    }

    [Event(EvtPushGroupFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "group push failed | ex_type={0} | ex_message={1}")]
    public void PushGroupFailedDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushGroupFailedDetail, ex_type, ex_message);
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

    [Event(EvtPushMultiFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Multi-light push failed")]
    public void PushMultiFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPushMultiFailed);
    }

    [Event(EvtPushMultiFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "multi-light push failed | ex_type={0} | ex_message={1}")]
    public void PushMultiFailedDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Push)) return;
        WriteEvent(EvtPushMultiFailedDetail, ex_type, ex_message);
    }

    [Event(EvtPushIncidentOpened,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Hue bridge pushes are failing — Ambient lighting is retrying")]
    public void PushIncidentOpened()
    {
        if (IsEnabled()) WriteEvent(EvtPushIncidentOpened);
    }

    [Event(EvtPushRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Hue bridge pushes recovered")]
    public void PushRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtPushRecovered);
    }

    [Event(EvtPushRejected,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "Hue rejected Ambient lighting — the pipeline is stopping")]
    public void PushRejected()
    {
        if (IsEnabled()) WriteEvent(EvtPushRejected);
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

    [Event(EvtHeartbeat,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "heartbeat | mode={0} | period_sec={1:F1} | target_hz={2} | effective_hz={3:F1} | ticks={4} | pushed={5} | dropped={6} | skipped_slots={7} | unmapped_lights={8}{9}")]
    public void Heartbeat(
        string mode,
        double period_sec,
        int target_hz,
        double effective_hz,
        int ticks,
        int pushed,
        int dropped,
        long skipped_slots,
        int unmapped_lights,
        string push_stats_suffix)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat)) return;
        WriteEvent(
            EvtHeartbeat,
            mode,
            period_sec,
            target_hz,
            effective_hz,
            ticks,
            pushed,
            dropped,
            skipped_slots,
            unmapped_lights,
            push_stats_suffix);
    }

}

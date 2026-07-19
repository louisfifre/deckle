using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    [Event(EvtCaptureLost,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Capture was lost — the engine is stopping")]
    public void CaptureLost()
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLost);
    }

    [Event(EvtFrameProcessingIncidentOpened,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Frame processing is failing — Ambient lighting is waiting to recover")]
    public void FrameProcessingIncidentOpened()
    {
        if (IsEnabled()) WriteEvent(EvtFrameProcessingIncidentOpened);
    }

    [Event(EvtFrameProcessingRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Frame processing recovered")]
    public void FrameProcessingRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtFrameProcessingRecovered);
    }

    [Event(EvtFrameProcessingFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "Frame processing did not recover — Ambient lighting is stopping")]
    public void FrameProcessingFailed()
    {
        if (IsEnabled()) WriteEvent(EvtFrameProcessingFailed);
    }

    [Event(EvtFrameProcessingEpisodeDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "frame processing episode | outcome={0} | failures={1} | active_failure_ms={2}")]
    public void FrameProcessingEpisodeDetail(string outcome, int failures, long active_failure_ms)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtFrameProcessingEpisodeDetail, outcome, failures, active_failure_ms);
    }

    [Event(EvtExternalChangeStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pipeline stopped by an external light change")]
    public void ExternalChangeStopped()
    {
        if (IsEnabled()) WriteEvent(EvtExternalChangeStopped);
    }

    [Event(EvtEventStreamSetupFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Hue event stream setup failed — external light change detection is disabled this session")]
    public void EventStreamSetupFailed()
    {
        if (IsEnabled()) WriteEvent(EvtEventStreamSetupFailed);
    }

    [Event(EvtEventStreamSetupFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "event stream setup failed | ex_type={0} | ex_message={1}")]
    public void EventStreamSetupFailedDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtEventStreamSetupFailedDetail, ex_type, ex_message);
    }

    [Event(EvtExternalChangeStoppedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "external change | v1_id={0} | resource_type={1} | age_ms={2} | on={3} | bri={4} | xy={5}")]
    public void ExternalChangeStoppedDetail(string v1_id, string resource_type, int age_ms, string on, string bri, string xy)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtExternalChangeStoppedDetail, v1_id, resource_type, age_ms, on, bri, xy);
    }

    [Event(EvtExternalChangeDecisionDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "external decision | v1_id={0} | resource_type={1} | age_ms={2} | event_on={3} | pushed_on={4} | event_bri={5} | pushed_bri={6} | event_xy={7} | pushed_xy={8} | delta_xy={9} | basis={10}")]
    public void ExternalChangeDecisionDetail(
        string v1_id,
        string resource_type,
        int age_ms,
        string event_on,
        string pushed_on,
        string event_bri,
        string pushed_bri,
        string event_xy,
        string pushed_xy,
        string delta_xy,
        string mismatch)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(
            EvtExternalChangeDecisionDetail,
            v1_id,
            resource_type,
            age_ms,
            event_on,
            pushed_on,
            event_bri,
            pushed_bri,
            event_xy,
            pushed_xy,
            delta_xy,
            mismatch);
    }

    [Event(EvtEchoIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "echo ignored | v1_id={0} | resource_type={1} | age_ms={2} | match=state")]
    public void EchoIgnored(string v1_id, string resource_type, int age_ms)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtEchoIgnored, v1_id, resource_type, age_ms);
    }

    // Per-light configuration dump: emitted in a loop, one line per light, with
    // ids and k=v — Verbose by nature, never a single human milestone. Demoted
    // from Info to Verbose for the Verbose/Info separation. The call site emits
    // it right after PipelineStarted, BEFORE AmbientCaptureGate opens, so the
    // line still passes the LogWindow drop filter (the gate is closed at that
    // point) — demotion costs no visibility there.
    [Event(EvtPipelinePerLightConfig,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "light cfg | id={0} | name={1} | zone={2} | brightness={3:F2} | controlled={4}")]
    public void PipelinePerLightConfig(string id, string name, string zone, double brightness, bool controlled)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelinePerLightConfig, id, name, zone, brightness, controlled);
    }

    // Consumer-side confirmation that the FrameSampler was rebuilt to match
    // a capture surface the service renegotiated mid-session (HDR↔SDR toggle
    // or resolution change). Info milestone — pairs with the Vision-side
    // CaptureFormatRenegotiated so the live window shows the full chain :
    // capture renegotiates → sampler rebuilt. mode = "HDR" | "SDR".
    [Event(EvtSamplerRebuilt,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Frame sampler rebuilt for the renegotiated capture surface")]
    public void SamplerRebuilt()
    {
        if (IsEnabled()) WriteEvent(EvtSamplerRebuilt);
    }

    [Event(EvtSamplerRebuiltDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "sampler rebuilt | mode={0}")]
    public void SamplerRebuiltDetail(string mode)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtSamplerRebuiltDetail, mode);
    }

    [Event(EvtSamplerRebuildFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Frame sampler rebuild failed after a capture renegotiation — output may stay frozen until restart")]
    public void SamplerRebuildFailed()
    {
        if (IsEnabled()) WriteEvent(EvtSamplerRebuildFailed);
    }

    [Event(EvtSamplerRebuildFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "sampler rebuild failed | ex_type={0} | ex_message={1}")]
    public void SamplerRebuildFailedDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtSamplerRebuildFailedDetail, ex_type, ex_message);
    }

    [Event(EvtPushLoopCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Push loop crashed")]
    public void PushLoopCrashed()
    {
        if (IsEnabled()) WriteEvent(EvtPushLoopCrashed);
    }

    [Event(EvtPushLoopCrashedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "push loop crashed | ex_type={0} | ex_message={1}")]
    public void PushLoopCrashedDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtPushLoopCrashedDetail, ex_type, ex_message);
    }

    [Event(EvtStateChangedSubscriberThrew,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A state-change subscriber threw")]
    public void StateChangedSubscriberThrew()
    {
        if (IsEnabled()) WriteEvent(EvtStateChangedSubscriberThrew);
    }

    [Event(EvtStateChangedSubscriberThrewDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "state change subscriber threw | ex_type={0} | ex_message={1}")]
    public void StateChangedSubscriberThrewDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtStateChangedSubscriberThrewDetail, ex_type, ex_message);
    }

    [Event(EvtMultiLightFallbackNoLights,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Multi-light requested but driver returned no lights — falling back to group push")]
    public void MultiLightFallbackNoLights()
    {
        if (IsEnabled()) WriteEvent(EvtMultiLightFallbackNoLights);
    }

    [Event(EvtMultiLightDriverIncompat,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Multi-light requested but the driver cannot address individual lights — falling back to group push")]
    public void MultiLightDriverIncompat()
    {
        if (IsEnabled()) WriteEvent(EvtMultiLightDriverIncompat);
    }

    [Event(EvtMultiLightDriverIncompatDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "multi-light driver incompatible | driver_type={0}")]
    public void MultiLightDriverIncompatDetail(string driver_type)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtMultiLightDriverIncompatDetail, driver_type);
    }

}

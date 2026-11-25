using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // The consequence of a capture that ended on its own. Vision already
    // logged the terminal Error with its cause; this Informational line only
    // tells the reader what it means for ambient lighting. reason is the
    // Vision CaptureStopReasons value the Stopped event carried.
    [Event(EvtCaptureLost,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Screen capture stopped, Ambient lighting is stopping")]
    public void CaptureLost(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureLost, reason);
    }

    // reason = sampler_failed (the sampler rejected a frame) | capture_failed
    // (the capture reported a frame-processing failure), the last cause seen.
    // The cause_* fields are that failure's own record, as the sampler or the
    // capture kept it: the stage that failed (texture_query, frame_consumer,
    // sampler_map, sampler_process, or unknown when none was kept), its hr and
    // its exception. Without them the line says a frame failed and never why.
    [Event(EvtFrameProcessingIncidentOpened,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Frame processing is failing, Ambient lighting is waiting to recover\nreason={0} | failures={1} | cause_stage={2} | cause_hr=0x{3:X8} | cause_ex_type={4} | cause_ex_message={5}")]
    public void FrameProcessingIncidentOpened(
        string reason, int failures,
        string cause_stage, int cause_hr, string cause_ex_type, string cause_ex_message)
    {
        if (IsEnabled())
            WriteEvent(EvtFrameProcessingIncidentOpened, reason, failures,
                cause_stage, cause_hr, cause_ex_type, cause_ex_message);
    }

    [Event(EvtFrameProcessingRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "Frame processing recovered")]
    public void FrameProcessingRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtFrameProcessingRecovered);
    }

    [Event(EvtFrameProcessingFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "Frame processing did not recover, Ambient lighting is stopping\nreason={0} | failures={1} | cause_stage={2} | cause_hr=0x{3:X8} | cause_ex_type={4} | cause_ex_message={5}")]
    public void FrameProcessingFailed(
        string reason, int failures,
        string cause_stage, int cause_hr, string cause_ex_type, string cause_ex_message)
    {
        if (IsEnabled())
            WriteEvent(EvtFrameProcessingFailed, reason, failures,
                cause_stage, cause_hr, cause_ex_type, cause_ex_message);
    }

    [Event(EvtFrameProcessingEpisodeDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "outcome={0} | failures={1} | active_failure_ms={2}")]
    public void FrameProcessingEpisodeDetail(string outcome, int failures, long active_failure_ms)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Ambient, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtFrameProcessingEpisodeDetail, outcome, failures, active_failure_ms);
    }

    [Event(EvtPipelinePerLightConfig,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "id={0} | name={1} | zone={2} | brightness={3:F2} | controlled={4}")]
    public void PipelinePerLightConfig(string id, string name, string zone, double brightness, bool controlled)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelinePerLightConfig, id, name, zone, brightness, controlled);
    }

    // Pairs with the Vision-side CaptureFormatRenegotiated so the log shows
    // the full chain: capture renegotiates (HDR↔SDR toggle or resolution
    // change), then the sampler is rebuilt.
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
           Message = "mode={0}")]
    public void SamplerRebuiltDetail(string mode)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtSamplerRebuiltDetail, mode);
    }

    [Event(EvtSamplerRebuildFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Frame sampler rebuild failed after a capture renegotiation, output may stay frozen until restart\nex_type={0} | ex_message={1}")]
    public void SamplerRebuildFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSamplerRebuildFailed, ex_type, ex_message);
    }

    // The engine stops itself after this event: a crashed loop no longer
    // pushes, so leaving the engine Running would show a zombie session.
    [Event(EvtPushLoopCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Pipeline | Keywords.Lifecycle),
           Message = "Ambient lighting push loop crashed, the pipeline is stopping\nex_type={0} | ex_message={1}")]
    public void PushLoopCrashed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPushLoopCrashed, ex_type, ex_message);
    }

    [Event(EvtStateChangedSubscriberThrew,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A state-change subscriber threw\nex_type={0} | ex_message={1}")]
    public void StateChangedSubscriberThrew(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtStateChangedSubscriberThrew, ex_type, ex_message);
    }

}

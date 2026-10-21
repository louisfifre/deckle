using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // ── AmbientEngine — lifecycle ───────────────────────────────────────

    [Event(EvtPipelineStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Ambient lighting starting")]
    public void PipelineStarting()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStarting);
    }

    [Event(EvtPipelineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Ambient lighting started")]
    public void PipelineStarted()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStarted);
    }

    [Event(EvtPipelineStartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "start | source={0} | output={1} | shape={2} | lights={3} | push_hz={4} | sampler_grid={5}x{6} | hdr={7}")]
    public void PipelineStartDetail(string source, string output, string shape, int lights, int push_hz, int grid_cols, int grid_rows, string hdr)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelineStartDetail, source, output, shape, lights, push_hz, grid_cols, grid_rows, hdr);
    }

    // reason = exception | invalid_pairing | an upstream abort reason
    // (capture_lost, external, push_rejected, push_loop_crashed, ...).
    [Event(EvtPipelineStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Ambient lighting failed to start | reason={0} | ex_type={1} | ex_message={2}")]
    public void PipelineStartFailed(string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStartFailed, reason, ex_type, ex_message);
    }

    // A start the caller cancelled is not a failure: it ends the Starting
    // milestone without raising an Error in the journal.
    [Event(EvtPipelineStartCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Ambient lighting start cancelled | elapsed_ms={0}")]
    public void PipelineStartCancelled(long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStartCancelled, elapsed_ms);
    }

    [Event(EvtPipelineStopping,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Ambient lighting stopping")]
    public void PipelineStopping()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopping);
    }

    // Emitted from the stop cleanup's finally so every Stopping has an end.
    // outcome = completed | failed; ex_type / ex_message name the first
    // cleanup failure, "none" when completed.
    [Event(EvtPipelineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Ambient lighting stopped | reason={0} | outcome={1} | duration_s={2:F1} | ex_type={3} | ex_message={4}")]
    public void PipelineStopped(string reason, string outcome, double duration_s, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopped, reason, outcome, duration_s, ex_type, ex_message);
    }

    [Event(EvtPipelineStopDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "stop | shape={0} | pushed={1} | dropped={2}")]
    public void PipelineStopDetail(string shape, long pushed, long dropped)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelineStopDetail, shape, pushed, dropped);
    }

    // component = capture | sampler | output | superseded_sampler. A release
    // failure can leave a native handle or a Hue streaming session behind.
    [Event(EvtResourceReleaseFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Lifecycle | Keywords.Resource),
           Message = "An Ambient lighting resource could not be released | component={0} | ex_type={1} | ex_message={2}")]
    public void ResourceReleaseFailed(string component, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtResourceReleaseFailed, component, ex_type, ex_message);
    }

    // step = previous_cleanup | push_loop | event_stream | cancel. These waits
    // and cancels swallow their failure because the lifecycle continues anyway.
    [Event(EvtPipelineStepFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "pipeline step failed | step={0} | ex_type={1} | ex_message={2}")]
    public void PipelineStepFailedDetail(string step, string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelineStepFailedDetail, step, ex_type, ex_message);
    }

    // Emitted by the app before it restarts a running engine because a
    // setting read only at start changed; the Stopping / Started milestones
    // narrate the restart itself. settings = comma-separated property names.
    [Event(EvtPipelineRestartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "restart | settings={0}")]
    public void PipelineRestartDetail(string settings)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelineRestartDetail, settings);
    }
}

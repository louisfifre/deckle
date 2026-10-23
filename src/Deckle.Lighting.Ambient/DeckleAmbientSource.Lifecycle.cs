using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // ── AmbientEngine — lifecycle ───────────────────────────────────────
    //
    // One run is one start and one stop. Every lifecycle event of a run
    // carries the same `work_id` (WorkId.Next(), taken when StartAsync begins),
    // so the file alone pairs a start with its end and tells two runs apart.
    // Every Starting ends in exactly one of Started, StartFailed or
    // StartCancelled; every Started ends in Stopping then Stopped.

    [Event(EvtPipelineStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient lighting starting")]
    public void PipelineStarting(string work_id)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStarting, work_id);
    }

    // What the run chose: output = the output class that was built, shape =
    // group | multi, lights = the controlled lights (0 in group mode),
    // push_hz = the push cadence, hdr = on | off.
    [Event(EvtPipelineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient lighting started")]
    public void PipelineStarted(string work_id, string output, string shape, int lights, int push_hz, string hdr)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStarted, work_id, output, shape, lights, push_hz, hdr);
    }

    [Event(EvtPipelineStartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "work_id={0} | source={1} | sampler_grid={2}x{3}")]
    public void PipelineStartDetail(string work_id, string source, int grid_cols, int grid_rows)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelineStartDetail, work_id, source, grid_cols, grid_rows);
    }

    // reason = exception | not_paired | bridge_ip_not_private | an upstream
    // abort reason (capture_lost, external, push_rejected, push_loop_crashed, ...).
    [Event(EvtPipelineStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient lighting failed to start\nwork_id={0} | reason={1} | ex_type={2} | ex_message={3}")]
    public void PipelineStartFailed(string work_id, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStartFailed, work_id, reason, ex_type, ex_message);
    }

    // A start the caller cancelled is not a failure: it ends the Starting
    // milestone without raising an Error in the log.
    [Event(EvtPipelineStartCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient lighting start cancelled")]
    public void PipelineStartCancelled(string work_id, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStartCancelled, work_id, elapsed_ms);
    }

    // reason = the engine's stop reason: user, or an upstream cause such as
    // capture_lost, external, push_rejected, frame_processing_failed.
    [Event(EvtPipelineStopping,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient lighting stopping")]
    public void PipelineStopping(string work_id, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopping, work_id, reason);
    }

    // Emitted from the stop cleanup's finally so every Stopping has an end.
    // outcome = completed | failed; ex_type / ex_message name the first
    // cleanup failure, "none" when completed. pushed / dropped are the run's
    // totals: the frames the bridge accepted and the ticks the change
    // threshold held back.
    [Event(EvtPipelineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient lighting stopped")]
    public void PipelineStopped(
        string work_id, string reason, string outcome, double duration_s,
        long pushed, long dropped, string ex_type, string ex_message)
    {
        if (IsEnabled())
            WriteEvent(EvtPipelineStopped, work_id, reason, outcome, duration_s, pushed, dropped, ex_type, ex_message);
    }

    // The run stopped while an incident it had opened was still open, so no
    // recovery will ever close it. incident = AmbientIncidentKinds. The stop
    // is the incident's end.
    [Event(EvtPipelineIncidentAbandoned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient lighting stopped with an incident still open")]
    public void PipelineIncidentAbandoned(string work_id, string incident, int failures, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineIncidentAbandoned, work_id, incident, failures, duration_ms);
    }

    // An upstream abort (capture lost, push rejected, ...) that the engine did
    // not act on: it was already Stopping or Off, or an earlier abort of the
    // same run is pending. why = already_stopping | abort_pending.
    [Event(EvtPipelineAbortIgnoredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0} | why={1}")]
    public void PipelineAbortIgnoredDetail(string reason, string why)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelineAbortIgnoredDetail, reason, why);
    }
    // component = capture | sampler | output | superseded_sampler. A release
    // failure can leave a native handle or a Hue streaming session behind.
    [Event(EvtResourceReleaseFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)(Keywords.Lifecycle | Keywords.Resource),
           Message = "An Ambient lighting resource could not be released\ncomponent={0} | ex_type={1} | ex_message={2}")]
    public void ResourceReleaseFailed(string component, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtResourceReleaseFailed, component, ex_type, ex_message);
    }

    // step = previous_cleanup | push_loop | event_stream | cancel. These waits
    // and cancels swallow their failure because the lifecycle continues anyway.
    [Event(EvtPipelineStepFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "step={0} | ex_type={1} | ex_message={2}")]
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
           Message = "settings={0}")]
    public void PipelineRestartDetail(string settings)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelineRestartDetail, settings);
    }
}

// Closed `incident` vocabulary of PipelineIncidentAbandoned.
public static class AmbientIncidentKinds
{
    // Hue pushes kept failing (PushIncidentOpened).
    public const string PushFailures = "push_failures";

    // Captured frames kept failing to process (FrameProcessingIncidentOpened).
    public const string FrameProcessing = "frame_processing";
}
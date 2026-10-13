using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class DeckleAmbientSource
{
    // ── AmbientEngine — lifecycle ───────────────────────────────────────

    [Event(EvtPipelineStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient lighting starting")]
    public void PipelineStarting()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStarting);
    }

    [Event(EvtPipelineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
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
        if (IsEnabled()) WriteEvent(EvtPipelineStartDetail, source, output, shape, lights, push_hz, grid_cols, grid_rows, hdr);
    }

    [Event(EvtPipelineStartFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pipeline failed to start")]
    public void PipelineStartFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStartFailed);
    }

    [Event(EvtPipelineStartFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "start failed | ex_type={0} | ex_message={1}")]
    public void PipelineStartFailedDetail(string ex_type, string ex_message)
    {
        if (!IsAmbientDetailEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle)) return;
        WriteEvent(EvtPipelineStartFailedDetail, ex_type, ex_message);
    }

    [Event(EvtPipelineStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient lighting stopped")]
    public void PipelineStopped()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopped);
    }

    [Event(EvtPipelineStopping,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Ambient lighting stopping")]
    public void PipelineStopping()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopping);
    }

    [Event(EvtPipelineStopDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "stop | reason={0} | shape={1} | duration_sec={2:F1} | pushed={3} | dropped={4}")]
    public void PipelineStopDetail(string reason, string shape, double duration_sec, long pushed, long dropped)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineStopDetail, reason, shape, duration_sec, pushed, dropped);
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

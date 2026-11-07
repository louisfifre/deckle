using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Vision;

// ── Interface capture ─────────────────────────────────────────────────────────
//
// The developer mode's capture of every Deckle window (InterfaceCaptureRun).
// A run is lasting work: its start and its end carry the same work_id and the
// Milestone tag, so the log alone pairs them. Each image is its own milestone
// line with its path, relative to the data root, because the capture is an act
// the person asked for and the log is where they find what it wrote. A window
// that could not be captured is a Warning: the run went on without it.
public sealed partial class DeckleVisionSource
{
    [Event(EvtInterfaceCaptureStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Milestone,
           Message = "Interface capture started")]
    public void InterfaceCaptureStarted(string work_id, string folder)
    {
        if (IsEnabled()) WriteEvent(EvtInterfaceCaptureStarted, work_id, folder);
    }

    [Event(EvtInterfaceWindowCaptured,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Tags = ObservationTags.Milestone,
           Message = "A window was captured to an image")]
    public void InterfaceWindowCaptured(string work_id, string path, int width, int height, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtInterfaceWindowCaptured, work_id, path, width, height, duration_ms);
    }

    [Event(EvtInterfaceWindowCaptureFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A window could not be captured, and the interface capture went on\nwork_id={0} | name={1} | step={2} | ex_type={3} | ex_message={4}")]
    public void InterfaceWindowCaptureFailed(string work_id, string name, string step, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtInterfaceWindowCaptureFailed, work_id, name, step, ex_type, ex_message);
    }

    // The run itself broke, not one window: the folder could not be made, or
    // walking the settings pages threw. The end line follows with the step as
    // its outcome.
    [Event(EvtInterfaceCaptureStopped,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Message = "The interface capture stopped before its end\nwork_id={0} | step={1} | ex_type={2} | ex_message={3}")]
    public void InterfaceCaptureStopped(string work_id, string step, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtInterfaceCaptureStopped, work_id, step, ex_type, ex_message);
    }

    // outcome: completed (every image written), partial (some failed),
    // failed (none written), or the step that stopped the run.
    [Event(EvtInterfaceCaptureCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Milestone,
           Message = "Interface capture ended")]
    public void InterfaceCaptureCompleted(string work_id, string outcome, int captured, int failed, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtInterfaceCaptureCompleted, work_id, outcome, captured, failed, duration_ms);
    }
}

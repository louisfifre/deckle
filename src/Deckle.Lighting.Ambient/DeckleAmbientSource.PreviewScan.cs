using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

// The preview scan: the screen capture the Settings preview runs while
// Ambient lighting is off, with no output. One scan is one piece of lasting
// work with its own work_id: every Starting ends in exactly one of
// StartFailed or Stopped. A scan that is asked for and does not start says
// why in Skipped.
public sealed partial class DeckleAmbientSource
{
    // capture_hz = the rate the capture was asked for (CaptureRateChosen
    // says how it was decided).
    [Event(EvtPreviewScanStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient preview scan starting")]
    public void PreviewScanStarting(string work_id, int capture_hz)
    {
        if (IsEnabled()) WriteEvent(EvtPreviewScanStarting, work_id, capture_hz);
    }

    // The preview goes on showing the display's outline and its zones, so
    // the failure degrades it rather than stopping anything: a Warning.
    [Event(EvtPreviewScanStartFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient preview could not capture the display, it shows the display's outline only\nwork_id={0} | ex_type={1} | ex_message={2}")]
    public void PreviewScanStartFailed(string work_id, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPreviewScanStartFailed, work_id, ex_type, ex_message);
    }

    // reason = AmbientPreviewScanEndReasons. outcome = completed | failed (a
    // release threw; ex_* carry the first failure). frames = frames the
    // capture delivered; first_frame_ms = from the start to the first frame
    // the sampler processed, -1 when none came.
    [Event(EvtPreviewScanStopped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient preview scan stopped")]
    public void PreviewScanStopped(
        string work_id, string reason, string outcome, double duration_s,
        long frames, long first_frame_ms, string ex_type, string ex_message)
    {
        if (IsEnabled())
            WriteEvent(EvtPreviewScanStopped, work_id, reason, outcome, duration_s, frames, first_frame_ms, ex_type, ex_message);
    }

    // A scan asked for while the engine owns the display, or is about to:
    // state = the engine's state at that moment.
    [Event(EvtPreviewScanSkipped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Ambient preview scan skipped, Ambient lighting holds the display")]
    public void PreviewScanSkipped(string state)
    {
        if (IsEnabled()) WriteEvent(EvtPreviewScanSkipped, state);
    }
}

// Why a preview scan ended, as PreviewScanStopped spells it.
public static class AmbientPreviewScanEndReasons
{
    // The preview closed or left the screen.
    public const string Closed = "closed";

    // Ambient lighting started and takes the display.
    public const string EngineStarting = "engine_starting";

    // The capture ended on its own; Deckle-Vision logged its cause.
    public const string CaptureLost = "capture_lost";

    // The display changed; the preview starts a new scan with the new value.
    public const string SettingsChanged = "settings_changed";
}

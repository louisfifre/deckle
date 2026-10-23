using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Hotkey / start gating ───────────────────────────────────────────

    [Event(EvtHotkeyToggleIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "state={0}")]
    public void HotkeyToggleIgnored(string state)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyToggleIgnored, state);
    }

    [Event(EvtHotkeyStartingCASLost,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The start transition was lost before recording began")]
    public void HotkeyStartingCASLost()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStartingCASLost);
    }

    // Opens the microphone incident, once until a capture succeeds. phase
    // (closed vocabulary): probe | capture. mmsys_err is the raw MMSYSERR code.
    [Event(EvtMicrophoneUnavailable,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The microphone is unavailable\nphase={0} | mmsys_err={1}")]
    public void MicrophoneUnavailable(string phase, int mmsys_err)
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneUnavailable, phase, mmsys_err);
    }

    [Event(EvtMicrophoneRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)(Keywords.Capture | Keywords.Lifecycle),
           Tags = ObservationTags.Milestone,
           Message = "Microphone access recovered")]
    public void MicrophoneRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneRecovered);
    }

    [Event(EvtRecordingLowAudio,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Low audio was surfaced to the user")]
    public void RecordingLowAudio()
    {
        if (IsEnabled()) WriteEvent(EvtRecordingLowAudio);
    }

    [Event(EvtAutoCalibrated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Level window auto-calibrated")]
    public void AutoCalibrated()
    {
        if (IsEnabled()) WriteEvent(EvtAutoCalibrated);
    }

    [Event(EvtAutoCalibratedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "new_min_dbfs={0:F0} | new_max_dbfs={1:F0} | session_count={2}")]
    public void AutoCalibratedDetail(double new_min_dbfs, double new_max_dbfs, int session_count)
    {
        if (IsEnabled()) WriteEvent(EvtAutoCalibratedDetail, new_min_dbfs, new_max_dbfs, session_count);
    }

    // ── Worker lifecycle ────────────────────────────────────────────────
    //
    // One worker thread per take or file batch. worker (closed vocabulary):
    // dictation | file. reached_idle false means Dispose won the race and the
    // engine did not return to Idle.

    [Event(EvtWorkerStarted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "worker={0}")]
    public void WorkerStarted(string worker)
    {
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline)) WriteEvent(EvtWorkerStarted, worker);
    }

    [Event(EvtWorkerStopped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "worker={0} | reached_idle={1}")]
    public void WorkerStopped(string worker, bool reached_idle)
    {
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline)) WriteEvent(EvtWorkerStopped, worker, reached_idle);
    }

    [Event(EvtPipelineCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The pipeline crashed\nex_type={0} | ex_message={1}")]
    public void PipelineCrashed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineCrashed, ex_type, ex_message);
    }

    // An exception swallowed during teardown, after the failure that matters
    // was already reported. site (closed vocabulary): prime_settle |
    // streaming_capture_teardown | file_decode_producer | recovery_temp_delete
    // | file_length.
    [Event(EvtTeardownExceptionIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "site={0} | ex_type={1} | ex_message={2}")]
    public void TeardownExceptionIgnored(string site, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtTeardownExceptionIgnored, site, ex_type, ex_message);
    }

    // A take that ends with nothing transcribed for a reason the user only
    // sees as a status line. reason (closed vocabulary): model_not_loaded.
    [Event(EvtTakeDropped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The take was dropped without transcription\nreason={0}")]
    public void TakeDropped(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtTakeDropped, reason);
    }

    // The take's text reached neither the clipboard nor the target window.
    // reason (closed vocabulary): raw_copy_failed. recovery_kept says whether
    // the dictation recovery file holds the take, which is then its only copy.
    [Event(EvtDictationNotDelivered,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "The dictation was not delivered\nreason={0} | recovery_kept={1}")]
    public void DictationNotDelivered(string reason, bool recovery_kept)
    {
        if (IsEnabled()) WriteEvent(EvtDictationNotDelivered, reason, recovery_kept);
    }
}

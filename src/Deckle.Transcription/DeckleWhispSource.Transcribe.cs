using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Hotkey / start gating ───────────────────────────────────────────

    [Event(EvtHotkeyToggleIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "toggle ignored | state={0}")]
    public void HotkeyToggleIgnored(string state)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyToggleIgnored, state);
    }

    // In-place clean (no params, no placeholders): the milestone is entirely a
    // human sentence; the lowercase phrasing and arrow shorthand were dropped.
    [Event(EvtHotkeyStartingCASLost,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The start transition was lost before recording began")]
    public void HotkeyStartingCASLost()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStartingCASLost);
    }

    [Event(EvtRecordingProbeFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The microphone probe failed")]
    public void RecordingProbeFailed()
    {
        if (IsEnabled()) WriteEvent(EvtRecordingProbeFailed);
    }

    [Event(EvtRecordingProbeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "probe failed | mmsys_err={0} | title={1}")]
    public void RecordingProbeFailedDetail(uint mmsys_err, string title)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingProbeFailedDetail, mmsys_err, title);
    }

    [Event(EvtRecordingMicError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The microphone failed during capture")]
    public void RecordingMicError()
    {
        if (IsEnabled()) WriteEvent(EvtRecordingMicError);
    }

    [Event(EvtRecordingMicErrorDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture mic error | mmsys_err={0} | title={1}")]
    public void RecordingMicErrorDetail(uint mmsys_err, string title)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingMicErrorDetail, mmsys_err, title);
    }

    [Event(EvtMicrophoneUnavailable,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "The microphone is unavailable")]
    public void MicrophoneUnavailable()
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneUnavailable);
    }

    [Event(EvtMicrophoneUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "microphone unavailable | phase={0} | mmsys_err={1}")]
    public void MicrophoneUnavailableDetail(string phase, uint mmsys_err)
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneUnavailableDetail, phase, mmsys_err);
    }

    [Event(EvtMicrophoneRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Microphone access recovered")]
    public void MicrophoneRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtMicrophoneRecovered);
    }

    // In-place clean (no params, no placeholders): lowercase implementation
    // phrasing recapitalized into a human sentence.
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
           Message = "auto-calibrated | new_min_dbfs={0:F0} | new_max_dbfs={1:F0} | session_count={2} | margins=p25-5dB/p90+5dB")]
    public void AutoCalibratedDetail(double new_min_dbfs, double new_max_dbfs, int session_count)
    {
        if (IsEnabled()) WriteEvent(EvtAutoCalibratedDetail, new_min_dbfs, new_max_dbfs, session_count);
    }

    [Event(EvtPipelineCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The pipeline crashed")]
    public void PipelineCrashed()
    {
        if (IsEnabled()) WriteEvent(EvtPipelineCrashed);
    }

    [Event(EvtPipelineCrashedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "pipeline crashed | ex_type={0} | ex_message={1}")]
    public void PipelineCrashedDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineCrashedDetail, ex_type, ex_message);
    }

}

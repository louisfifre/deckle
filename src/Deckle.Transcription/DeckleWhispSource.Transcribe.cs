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

    [Event(EvtHotkeyStartingCASLost,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "starting → recording CAS lost (likely Dispose)")]
    public void HotkeyStartingCASLost()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStartingCASLost);
    }

    [Event(EvtRecordingProbeFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "probe MMSYSERR={0} — {1}")]
    public void RecordingProbeFailed(uint mmsys_err, string title)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingProbeFailed, mmsys_err, title);
    }

    [Event(EvtRecordingMicError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "capture mic error MMSYSERR={0} — {1}")]
    public void RecordingMicError(uint mmsys_err, string title)
    {
        if (IsEnabled()) WriteEvent(EvtRecordingMicError, mmsys_err, title);
    }

    [Event(EvtRecordingLowAudio,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "low audio overlay surfaced")]
    public void RecordingLowAudio()
    {
        if (IsEnabled()) WriteEvent(EvtRecordingLowAudio);
    }

    [Event(EvtAutoCalibrated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Auto-calibrated level window: Min={0:F0} Max={1:F0} dBFS (median over {2} sessions, p25-5dB / p90+5dB margins)")]
    public void AutoCalibrated(double new_min_dbfs, double new_max_dbfs, int session_count)
    {
        if (IsEnabled()) WriteEvent(EvtAutoCalibrated, new_min_dbfs, new_max_dbfs, session_count);
    }

    [Event(EvtPipelineCrashed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "pipeline crashed: {0}: {1}")]
    public void PipelineCrashed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPipelineCrashed, ex_type, ex_message);
    }

    // ── Transcribe ──────────────────────────────────────────────────────

    [Event(EvtTranscribeStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcribing")]
    public void TranscribeStarted()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeStarted);
    }

    [Event(EvtTranscribeStartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "start | audio_sec={0:F1} | samples={1} | strategy={2}")]
    public void TranscribeStartDetail(double audio_sec, int samples, string strategy)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeStartDetail, audio_sec, samples, strategy);
    }

    [Event(EvtTranscribeParams,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "params | {0}")]
    public void TranscribeParams(string params_text)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeParams, params_text);
    }

    [Event(EvtTranscribePrompt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "prompt | len={0} | carry={1} | text=\"{2}\"")]
    public void TranscribePrompt(int prompt_len, bool carry, string preview)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribePrompt, prompt_len, carry, preview);
    }

    [Event(EvtTranscribeEmpty,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "empty audio buffer, nothing to transcribe")]
    public void TranscribeEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeEmpty);
    }

    [Event(EvtTranscribeFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "whisper_full failed | result={0}")]
    public void TranscribeFailed(int result)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeFailed, result);
    }

    [Event(EvtTranscribeCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcription complete ({0} seg)")]
    public void TranscribeCompleted(int n_seg)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeCompleted, n_seg);
    }

    [Event(EvtTranscribeCompleteDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "complete | whisper_ms={0} | n_seg={1} | chars={2}")]
    public void TranscribeCompleteDetail(long whisper_ms, int n_seg, int chars)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeCompleteDetail, whisper_ms, n_seg, chars);
    }

    [Event(EvtTranscribeRepetitionLoop,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "repetition loop detected — {0} identical segments ('{1}'); requesting whisper to abort")]
    public void TranscribeRepetitionLoop(int streak, string preview)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeRepetitionLoop, streak, preview);
    }

    [Event(EvtTranscribeSkipped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "skip transcribe | state={0}")]
    public void TranscribeSkipped(string state)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeSkipped, state);
    }

    // ── Streaming pipeline ──────────────────────────────────────────────

    [Event(EvtStreamingDrained,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Streaming drained | utterances={0} | total_ms={1} | n_seg={2}")]
    public void StreamingDrained(int n_utterances, long total_ms, int n_seg)
    {
        if (IsEnabled()) WriteEvent(EvtStreamingDrained, n_utterances, total_ms, n_seg);
    }

    [Event(EvtUtteranceSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "utterance #{0} failed, dictation continues | {1}: {2}")]
    public void UtteranceSkipped(int index, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtUtteranceSkipped, index, ex_type, ex_message);
    }

    // ── Segment callback ────────────────────────────────────────────────

    [Event(EvtSegmentEmitted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}")]
    public void SegmentEmitted(string segment_line)
    {
        if (IsEnabled()) WriteEvent(EvtSegmentEmitted, segment_line);
    }

    [Event(EvtSegmentCallbackThrew,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "{0}: {1}")]
    public void SegmentCallbackThrew(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackThrew, ex_type, ex_message);
    }
}
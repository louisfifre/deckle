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

    // In-place clean (no params, no placeholders): lowercase phrasing
    // recapitalized into a human sentence.
    [Event(EvtTranscribeEmpty,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "There was no audio to transcribe")]
    public void TranscribeEmpty()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeEmpty);
    }

    [Event(EvtTranscribeFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcription failed")]
    public void TranscribeFailed()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeFailed);
    }

    [Event(EvtTranscribeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "whisper_full failed | result={0}")]
    public void TranscribeFailedDetail(int result)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeFailedDetail, result);
    }

    // Milestone drops the segment count; the existing TranscribeCompleteDetail
    // (whisper_ms | n_seg | chars) is its Verbose mirror, already following at
    // the call site.
    [Event(EvtTranscribeCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Transcription complete")]
    public void TranscribeCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeCompleted);
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
           Message = "A repetition loop was detected and transcription was aborted")]
    public void TranscribeRepetitionLoop()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeRepetitionLoop);
    }

    [Event(EvtTranscribeRepetitionLoopDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "repetition loop | streak={0} | period={1} | text=\"{2}\"")]
    public void TranscribeRepetitionLoopDetail(int streak, int period, string preview)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeRepetitionLoopDetail, streak, period, preview);
    }

    [Event(EvtTranscribeHallucinationFiltered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A known hallucination was filtered out")]
    public void TranscribeHallucinationFiltered()
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeHallucinationFiltered);
    }

    [Event(EvtTranscribeHallucinationFilteredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "hallucination filtered | text=\"{0}\"")]
    public void TranscribeHallucinationFilteredDetail(string preview)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeHallucinationFilteredDetail, preview);
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
           Message = "Streaming complete")]
    public void StreamingDrained()
    {
        if (IsEnabled()) WriteEvent(EvtStreamingDrained);
    }

    [Event(EvtStreamingDrainedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "streaming complete | utterances={0} | audio_sec={1:F1} | whisper_ms={2} | words={3} | n_seg={4}")]
    public void StreamingDrainedDetail(int n_utterances, double audio_sec, long whisper_ms, int words, int n_seg)
    {
        if (IsEnabled()) WriteEvent(EvtStreamingDrainedDetail, n_utterances, audio_sec, whisper_ms, words, n_seg);
    }

    [Event(EvtUtteranceSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "An utterance failed and dictation continued")]
    public void UtteranceSkipped()
    {
        if (IsEnabled()) WriteEvent(EvtUtteranceSkipped);
    }

    [Event(EvtUtteranceSkippedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "utterance skipped | index={0} | ex_type={1} | ex_message={2}")]
    public void UtteranceSkippedDetail(int index, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtUtteranceSkippedDetail, index, ex_type, ex_message);
    }

    // Producer side: one event per utterance the segmenter cuts off the live
    // stream. hangover_used_ms is the silence the state machine actually waited
    // before deciding the utterance had ended — when it shrinks below the
    // configured max, the dynamic ramp is in action.
    [Event(EvtSegmenterUtteranceEmitted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "utterance #{0} cut | voiced_frames={1} | kept_frames={2} | start={3:F2}s | end={4:F2}s | hangover_used_ms={5}")]
    public void SegmenterUtteranceEmitted(int index, int voiced_frames, int kept_frames, double start_sec, double end_sec, int hangover_used_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSegmenterUtteranceEmitted, index, voiced_frames, kept_frames, start_sec, end_sec, hangover_used_ms);
    }

    // 1 Hz recap of the streaming socle during capture. Reads the segmenter's
    // live state, the consumer-side backlog, and the current capture RMS at one
    // point in time, so the log shows the producer/consumer balance, the dynamic
    // hangover in action, and the live mic level without spamming per-frame.
    [Event(EvtStreamingHeartbeat,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "heartbeat | state={0,-8} | rms_dbfs={1,6:F1} | utterance_ms={2,6} | hangover_required_ms={3,5} | backlog={4,3} | emitted={5,3} | recording_sec={6,5}")]
    public void StreamingHeartbeat(string state, double rms_dbfs, int utterance_ms, int hangover_required_ms, int backlog, int emitted, int recording_sec)
    {
        if (IsEnabled()) WriteEvent(EvtStreamingHeartbeat, state, rms_dbfs, utterance_ms, hangover_required_ms, backlog, emitted, recording_sec);
    }

    // Milestone: human-readable jalon at the start of a streaming take, paired
    // with TranscribeStarted on the monolithic path. The LogWindow shows this
    // line; the next SegmenterSettingsSnapshot details which parameters the
    // socle is running with.
    [Event(EvtStreamingPipelineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Streaming pipeline started")]
    public void StreamingPipelineStarted()
    {
        if (IsEnabled()) WriteEvent(EvtStreamingPipelineStarted);
    }

    // Verbose snapshot of the effective segmenter parameters at the start of a
    // take, so a log session is reproducible regardless of whether the values
    // come from the hard-coded defaults or a settings.json override.
    [Event(EvtSegmenterSettingsSnapshot,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "segmenter | threshold_dbfs={0:F1} | hangover_max_ms={1} | hangover_min_ms={2} | ramp_start_ms={3} | ramp_end_ms={4} | margin_ms={5} | min_utterance_ms={6}")]
    public void SegmenterSettingsSnapshot(double threshold_dbfs, int hangover_max_ms, int hangover_min_ms, int ramp_start_ms, int ramp_end_ms, int margin_ms, int min_utterance_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSegmenterSettingsSnapshot, threshold_dbfs, hangover_max_ms, hangover_min_ms, ramp_start_ms, ramp_end_ms, margin_ms, min_utterance_ms);
    }

    // Producer side: emitted when the segmenter discards a voiced span shorter
    // than MinUtteranceMs (a noise blip). Helps diagnose a threshold that
    // catches the wrong things — a flurry of blips says the threshold is too
    // permissive for the current room.
    [Event(EvtSegmenterBlipDropped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "blip dropped | voiced_frames={0} | voiced_ms={1}")]
    public void SegmenterBlipDropped(int voiced_frames, int voiced_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSegmenterBlipDropped, voiced_frames, voiced_ms);
    }

    // Consumer side: one event per utterance transcribed. backlog_after is the
    // remaining channel depth right after this utterance was dequeued — drops
    // toward 0 mean the consumer has caught up with the producer.
    [Event(EvtConsumerUtterance,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "utterance #{0} consumed | whisper_ms={1} | words={2} | seg={3} | backlog_after={4} | aborted={5}")]
    public void ConsumerUtterance(int index, long whisper_ms, int words, int segments, int backlog_after, bool aborted)
    {
        if (IsEnabled()) WriteEvent(EvtConsumerUtterance, index, whisper_ms, words, segments, backlog_after, aborted);
    }

    // ── Segment callback ────────────────────────────────────────────────

    [Event(EvtSegmentEmitted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline | KwTranscript,
           Message = "{0}")]
    public void SegmentEmitted(string segment_line)
    {
        if (IsEnabled()) WriteEvent(EvtSegmentEmitted, segment_line);
    }

    [Event(EvtSegmentCallbackThrew,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The segment callback threw")]
    public void SegmentCallbackThrew()
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackThrew);
    }

    [Event(EvtSegmentCallbackThrewDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "segment callback threw | ex_type={0} | ex_message={1}")]
    public void SegmentCallbackThrewDetail(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackThrewDetail, ex_type, ex_message);
    }
}
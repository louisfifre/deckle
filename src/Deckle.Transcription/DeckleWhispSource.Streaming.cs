using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
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
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtStreamingDrainedDetail, n_utterances, audio_sec, whisper_ms, words, n_seg);
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
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtUtteranceSkippedDetail, index, ex_type, ex_message);
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
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtSegmenterUtteranceEmitted, index, voiced_frames, kept_frames, start_sec, end_sec, hangover_used_ms);
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
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtStreamingHeartbeat, state, rms_dbfs, utterance_ms, hangover_required_ms, backlog, emitted, recording_sec);
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
           Message = "segmenter | threshold_dbfs={0:F1} | hangover_max_ms={1} | hangover_min_ms={2} | ramp_start_ms={3} | ramp_end_ms={4} | curve=({5:F2},{6:F2},{7:F2},{8:F2}) | margin_ms={9} | min_utterance_ms={10}")]
    public void SegmenterSettingsSnapshot(
        double threshold_dbfs,
        int hangover_max_ms,
        int hangover_min_ms,
        int ramp_start_ms,
        int ramp_end_ms,
        double curve_x1,
        double curve_y1,
        double curve_x2,
        double curve_y2,
        int margin_ms,
        int min_utterance_ms)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(
                EvtSegmenterSettingsSnapshot,
                threshold_dbfs,
                hangover_max_ms,
                hangover_min_ms,
                ramp_start_ms,
                ramp_end_ms,
                curve_x1,
                curve_y1,
                curve_x2,
                curve_y2,
                margin_ms,
                min_utterance_ms);
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
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtSegmenterBlipDropped, voiced_frames, voiced_ms);
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
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtConsumerUtterance, index, whisper_ms, words, segments, backlog_after, aborted);
    }

}

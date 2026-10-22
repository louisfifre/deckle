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
           Message = "streaming complete | utterances={0} | audio_s={1:F1} | whisper_ms={2} | words={3} | n_seg={4}")]
    public void StreamingDrainedDetail(int n_utterances, double audio_s, long whisper_ms, int words, int n_seg)
    {
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtStreamingDrainedDetail, n_utterances, audio_s, whisper_ms, words, n_seg);
    }

    // ── Utterance failures: one incident per take ───────────────────────
    //
    // The first failed utterance of a take opens the incident with its cause;
    // later ones stay Verbose; the next utterance that transcribes closes it.

    [Event(EvtUtteranceSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "An utterance failed and dictation continued | index={0} | ex_type={1} | ex_message={2}")]
    public void UtteranceSkipped(int index, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtUtteranceSkipped, index, ex_type, ex_message);
    }

    [Event(EvtUtteranceSkippedRepeat,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "utterance skipped again | index={0} | ex_type={1} | ex_message={2}")]
    public void UtteranceSkippedRepeat(int index, string ex_type, string ex_message)
    {
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtUtteranceSkippedRepeat, index, ex_type, ex_message);
    }

    [Event(EvtUtterancesRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Journal,
           Message = "Utterances transcribe again | skipped={0}")]
    public void UtterancesRecovered(int skipped)
    {
        if (IsEnabled()) WriteEvent(EvtUtterancesRecovered, skipped);
    }

    // Utterances the segmenter cut but the consumer never transcribed.
    // reason (closed vocabulary): prime_failed.
    [Event(EvtUtterancesDiscarded,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Utterances were discarded without transcription | reason={0} | discarded={1}")]
    public void UtterancesDiscarded(string reason, int discarded)
    {
        if (IsEnabled()) WriteEvent(EvtUtterancesDiscarded, reason, discarded);
    }

    // An utterance Whisper decoded but whose result no speaker could have
    // produced from that audio, left out of the text. Journal: it is work
    // declared as not done, with the numbers that justified it.
    // reason (closed vocabulary): token_rate, low_confidence.
    [Event(EvtUtterancePhantomRejected,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Journal,
           Message = "Utterance rejected as a phantom | index={0} | reason={1} | audio_ms={2} | text_tokens={3} | tokens_per_s={4:F1} | avg_p={5:F3} | min_p={6:F3}")]
    public void UtterancePhantomRejected(
        int index, string reason, int audio_ms, int text_tokens,
        double tokens_per_s, double avg_p, double min_p)
    {
        if (IsEnabled())
            WriteEvent(EvtUtterancePhantomRejected, index, reason, audio_ms, text_tokens, tokens_per_s, avg_p, min_p);
    }

    // The level of the audio handed to the backend, after preprocessing and the
    // VAD trim, so it reads against the pre-processing input level. audio_ms and
    // the levels describe the utterance alone; pad_ms is the noise tail appended
    // after it, so the backend receives audio_ms + pad_ms.
    [Event(EvtUtteranceSentToBackend,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "utterance sent to backend | index={0} | audio_ms={1} | rms_dbfs={2:F1} | peak_dbfs={3:F1} | pad_ms={4}")]
    public void UtteranceSentToBackend(int index, int audio_ms, double rms_dbfs, double peak_dbfs, int pad_ms)
    {
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtUtteranceSentToBackend, index, audio_ms, rms_dbfs, peak_dbfs, pad_ms);
    }

    // hangover_used_ms is the silence the segmenter actually waited before
    // closing the utterance; below the configured max, the dynamic ramp is acting.
    [Event(EvtSegmenterUtteranceEmitted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "utterance cut | index={0} | voiced_frames={1} | kept_frames={2} | start_s={3:F2} | end_s={4:F2} | hangover_used_ms={5}")]
    public void SegmenterUtteranceEmitted(int index, int voiced_frames, int kept_frames, double start_s, double end_s, int hangover_used_ms)
    {
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtSegmenterUtteranceEmitted, index, voiced_frames, kept_frames, start_s, end_s, hangover_used_ms);
    }

    // Once per second during streaming capture, instead of one event per frame.
    [Event(EvtStreamingHeartbeat,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "heartbeat | state={0,-8} | rms_dbfs={1,6:F1} | utterance_ms={2,6} | hangover_required_ms={3,5} | backlog={4,3} | emitted={5,3} | recording_s={6,5}")]
    public void StreamingHeartbeat(string state, double rms_dbfs, int utterance_ms, int hangover_required_ms, int backlog, int emitted, int recording_s)
    {
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtStreamingHeartbeat, state, rms_dbfs, utterance_ms, hangover_required_ms, backlog, emitted, recording_s);
    }

    [Event(EvtStreamingPipelineStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Streaming pipeline started")]
    public void StreamingPipelineStarted()
    {
        if (IsEnabled()) WriteEvent(EvtStreamingPipelineStarted);
    }

    // Logs the effective segmenter parameters at the start of each segmented
    // session, so a log is reproducible whether the values come from the
    // defaults or a settings.json override.
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
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline))
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

    // A voiced span shorter than MinUtteranceMs, discarded as a noise blip. A
    // flurry of blips says the threshold is too permissive for the room.
    [Event(EvtSegmenterBlipDropped,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "blip dropped | voiced_frames={0} | voiced_ms={1}")]
    public void SegmenterBlipDropped(int voiced_frames, int voiced_ms)
    {
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtSegmenterBlipDropped, voiced_frames, voiced_ms);
    }

    // backlog_after is the channel depth right after this utterance was
    // dequeued; near 0 means the consumer has caught up with the producer.
    [Event(EvtConsumerUtterance,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "utterance #{0} consumed | whisper_ms={1} | words={2} | seg={3} | backlog_after={4} | aborted={5}")]
    public void ConsumerUtterance(int index, long whisper_ms, int words, int segments, int backlog_after, bool aborted)
    {
        if (IsTakeDetailEnabled((EventKeywords)Keywords.Pipeline))
            WriteEvent(EvtConsumerUtterance, index, whisper_ms, words, segments, backlog_after, aborted);
    }
}

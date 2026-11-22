using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription.Parakeet;

// Parakeet backend provider: the model it loads (which files, which device, why
// not) and the native decode calls. The transcription engine above it logs through
// Deckle-Whisp and tells the story of a take; this provider carries the facts
// behind it.
//
// Transcript text never enters an event here: segment events carry counts and
// timings only (and those come from Deckle-Whisp).
[EventSource(Name = "Deckle-Parakeet")]
public sealed class DeckleParakeetSource : DeckleEventSource
{
    public static readonly DeckleParakeetSource Log = new();

    private DeckleParakeetSource() { }

    // ── EventIds: sequential from 1, never reused ───────────────────────
    public const int EvtModelLoadStarting          = 1;
    public const int EvtModelLoadStartingDetail    = 2;
    public const int EvtModelLoadFailed            = 3;
    public const int EvtModelLoadFailedDetail      = 4;
    public const int EvtModelOnGpu                 = 5;
    public const int EvtModelOnCpu                 = 6;
    public const int EvtModelGpuUnavailable        = 7;
    public const int EvtModelLoadComplete          = 8;
    public const int EvtTranscribeFailed           = 9;
    public const int EvtSegmentCallbackFailed      = 10;
    public const int EvtSegmentCallbackFailedDetail = 11;
    public const int EvtSegmentCallbackRecovered   = 12;
    public const int EvtTranscribeSummary          = 13;
    public const int EvtChunkDecoded               = 14;
    public const int EvtLexiconUnavailable         = 15;

    // ── Model load ──────────────────────────────────────────────────────

    // Every load, ungated: which model, which encoder precision and whether the
    // settings asked for the GPU are the first questions of a log read elsewhere.
    [Event(EvtModelLoadStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Loading the Parakeet model")]
    public void ModelLoadStarting(string model, string encoder_precision, bool use_gpu)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadStarting, model, encoder_precision, use_gpu);
    }

    [Event(EvtModelLoadStartingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "path={0} | encoder_mb={1:F1}")]
    public void ModelLoadStartingDetail(string path, double encoder_mb)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadStartingDetail, PathRedaction.Redact(path), encoder_mb);
    }

    // Once per incident: the engine retries the load at every take, and its own
    // ModelUnavailable/ModelRecovered pair is the visible incident, with the same
    // reason through ModelLoadResult.ErrorReason. reason (closed vocabulary):
    // file_not_found | init_failed.
    [Event(EvtModelLoadFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0} | model={1}")]
    public void ModelLoadFailed(string reason, string model)
    {
        if (IsEnabled()) WriteEvent(EvtModelLoadFailed, reason, model);
    }

    // Every failed attempt. path is the file the load stopped on (missing file) or
    // the model folder; ex_type is empty for a missing file.
    [Event(EvtModelLoadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0} | path={1} | elapsed_ms={2} | use_gpu={3} | ex_type={4} | ex_message={5}")]
    public void ModelLoadFailedDetail(
        string reason, string path, long elapsed_ms, bool use_gpu, string ex_type, string ex_message)
    {
        if (IsEnabled())
            WriteEvent(EvtModelLoadFailedDetail, reason, PathRedaction.Redact(path), elapsed_ms, use_gpu, ex_type,
                PathRedaction.Redact(ex_message));
    }

    // device is the adapter the encoder runs on, as the DirectML index.
    [Event(EvtModelOnGpu,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The Parakeet encoder runs on the GPU")]
    public void ModelOnGpu(string device, string encoder_precision)
    {
        if (IsEnabled()) WriteEvent(EvtModelOnGpu, device, encoder_precision);
    }

    [Event(EvtModelOnCpu,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The Parakeet encoder runs on the CPU, as the settings ask")]
    public void ModelOnCpu(string encoder_precision)
    {
        if (IsEnabled()) WriteEvent(EvtModelOnCpu, encoder_precision);
    }

    // The GPU was asked for and the DirectML session could not be created, so the
    // encoder runs on the CPU; the load still succeeds. Once per incident, closed
    // by the next ModelOnGpu or ModelOnCpu. reason (closed vocabulary):
    // device_unavailable.
    [Event(EvtModelGpuUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The GPU could not be used, the Parakeet encoder runs on the CPU\nreason={0} | ex_type={1}")]
    public void ModelGpuUnavailable(string reason, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtModelGpuUnavailable, reason, ex_type);
    }

    // Every successful load, including those inside an open incident:
    // fallback_reason repeats the incident's cause, empty when none.
    [Event(EvtModelLoadComplete,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "load_ms={0} | accelerator={1} | encoder_precision={2} | use_gpu={3} | fallback_reason={4}")]
    public void ModelLoadComplete(
        long load_ms, string accelerator, string encoder_precision, bool use_gpu, string fallback_reason)
    {
        if (IsEnabled())
            WriteEvent(EvtModelLoadComplete, load_ms, accelerator, encoder_precision, use_gpu, fallback_reason);
    }

    // ── Decode ──────────────────────────────────────────────────────────

    // reason (closed vocabulary): no_context (no model loaded when the call came)
    // | inference_failed (a session or the decoding threw; step says which:
    // encode | decode). The same reason travels up in
    // TranscriptionResult.ErrorReason, and the engine's failure event is the
    // visible incident; ex_type and ex_message are the cause behind it.
    [Event(EvtTranscribeFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "reason={0} | step={1} | ex_type={2} | ex_message={3}")]
    public void TranscribeFailed(string reason, string step, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtTranscribeFailed, reason, step, ex_type, PathRedaction.Redact(ex_message));
    }

    // A sink that throws once usually throws at every take: one Error per
    // incident, the raw failures in the gated detail.
    [Event(EvtSegmentCallbackFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The segment callback failed, segments may be missing\nex_type={0} | ex_message={1}")]
    public void SegmentCallbackFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackFailed, ex_type, PathRedaction.Redact(ex_message));
    }

    [Event(EvtSegmentCallbackFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "failures={0} | ex_type={1} | ex_message={2}")]
    public void SegmentCallbackFailedDetail(int failures, string ex_type, string ex_message)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtSegmentCallbackFailedDetail, failures, ex_type, PathRedaction.Redact(ex_message));
    }

    [Event(EvtSegmentCallbackRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "The segment callback works again")]
    public void SegmentCallbackRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtSegmentCallbackRecovered);
    }

    // The one record every call leaves, kept by the always-on log. encode_ms is the
    // mel front end plus the encoder, decode_ms the transducer loop, summed over
    // the chunks; aborted says the call stopped on cancellation, chunks_done how
    // many of the chunks it had were finished. boosted_tokens counts the tokens the
    // lexicon made the decoder pick over the model's own first choice.
    [Event(EvtTranscribeSummary,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Summary,
           Version = 1,
           Message = "Parakeet transcribed the audio")]
    public void TranscribeSummary(
        double audio_s, int chunks, int chunks_done, int frames, int tokens, int segments,
        long encode_ms, long decode_ms, bool aborted, int boosted_tokens)
    {
        if (IsEnabled())
            WriteEvent(EvtTranscribeSummary, audio_s, chunks, chunks_done, frames, tokens, segments,
                encode_ms, decode_ms, aborted, boosted_tokens);
    }

    // The lexicon could not be prepared: this call decodes without it, as if the
    // list were empty, and the next call tries again. Only the exception type and
    // message: the terms are the person's own words.
    [Event(EvtLexiconUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The dictation lexicon could not be prepared, this take runs without it\nex_type={0} | ex_message={1}")]
    public void LexiconUnavailable(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtLexiconUnavailable, ex_type, PathRedaction.Redact(ex_message));
    }

    // A table row: one per chunk, so the chunks of a long take compare column by
    // column. offset_s is where the chunk starts in the call's audio.
    [Event(EvtChunkDecoded,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "chunk={0,3} | chunks={1,3} | offset_s={2,7:F2} | audio_s={3,6:F2} | frames={4,5} | tokens={5,5} | encode_ms={6,6} | decode_ms={7,6}")]
    public void ChunkDecoded(
        int chunk, int chunks, double offset_s, double audio_s, int frames, int tokens, long encode_ms, long decode_ms)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                this,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline)) return;
        WriteEvent(EvtChunkDecoded, chunk, chunks, offset_s, audio_s, frames, tokens, encode_ms, decode_ms);
    }
}

using System.Text;
using System.Threading.Channels;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Transcription.Engine;
using Deckle.Transcription.Streaming;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── StreamingPipeline partial — producer/consumer over utterances ──────────
    //
    // Streaming strategy: an energy segmenter cuts the live capture stream into
    // utterances (on the worker thread, via the Frame event), a Channel hands
    // them to a consumer task that transcribes each one as it arrives and
    // accumulates the text. At Stop the producer flushes the open utterance and
    // completes the channel; the consumer drains whatever is queued, then the
    // assembled text flows into the shared FinalizeTranscription. Most utterances
    // are already transcribed before Stop, so the perceived Stop latency is just
    // the remaining backlog — and a decoder loop is confined to one utterance
    // instead of poisoning the whole take.
    //
    // Threading: capture (Record) blocks the worker thread = the producer; the
    // segmenter is therefore single-threaded (Push/Flush both on the worker
    // thread, synchronously inside the capture loop). The consumer runs on a
    // separate task and is the only backend caller at a time (sequential loop),
    // so the single-context invariant holds. The Channel is the one thread-safe
    // hand-off between them.
    //
    // Two tokens: producerCt (Stop + Dispose) stops capture; drainCt (Dispose
    // only) aborts the consumer's in-flight inference. On Stop the consumer is
    // NOT cancelled, so the queued tail transcribes losslessly; on Dispose it is,
    // so the worker join stays bounded.
    private async Task<PipelineProduction?> ProduceStreamingAsync(
        CancellationToken producerCt, CancellationToken drainCt)
    {
        var channel = Channel.CreateUnbounded<Utterance>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var segmenter = new EnergySegmenter(
            _host.Transcription.Streaming.Segmenter,
            u => channel.Writer.TryWrite(u)); // unbounded → never blocks the producer

        void OnFrame(CaptureFrame f) => segmenter.Push(f);

        // The consumer must be live BEFORE capture starts — frames (and the
        // utterances they yield) arrive during Record.
        Task<StreamingConsumeResult> consumer =
            Task.Run(() => ConsumeUtterancesAsync(channel.Reader, drainCt));

        _capture.Frame += OnFrame;

        CaptureResult capture;
        try
        {
            // Blocks on the worker thread until Stop / cap / Dispose = the producer.
            capture = _capture.Record(_recordingHost, producerCt);
        }
        finally
        {
            // No more frames will fire once Record has returned (the drain pass
            // ran inside it, on this same thread). Flush the open utterance, then
            // seal the channel so the consumer's ReadAllAsync completes.
            _capture.Frame -= OnFrame;
            segmenter.Flush();
            channel.Writer.Complete();
        }

        _recordDrainDuration = capture.DrainDuration;

        // Await the consumer drain. On Stop it finishes naturally (lossless tail);
        // on Dispose drainCt aborted it mid-inference → OCE → abandon.
        StreamingConsumeResult consumed;
        try
        {
            consumed = await consumer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DeckleWhispSource.Log.TranscribeSkipped("disposed");
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        // Mark the perceived Stop→drained latency (the streaming priority metric).
        // On a user Stop _stopToPipelineSw was started by RequestToggle; on cap-hit
        // it was never started, so it stays 0 (matches the monolithic cap-hit path).
        if (_stopToPipelineSw is { IsRunning: true }) _stopToPipelineSw.Stop();

        if (capture.Outcome == CaptureOutcome.MicError)
        {
            var (title, body) = LocalizeMicError(MicErrorKind.Unavailable, capture.MmsysErr);
            DeckleWhispSource.Log.RecordingMicError(capture.MmsysErr, title);
            EmitUserFeedback(FB_ERROR, title, body, FB_REPLACEMENT);
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        if (capture.Outcome == CaptureOutcome.CapHit)
        {
            Interlocked.CompareExchange(
                ref _state, (int)PipelineState.Stopping, (int)PipelineState.Recording);
        }

        if (capture.Telemetry is not null)
        {
            TryAutoCalibrate(capture.Telemetry);
        }

        // Stopping → Transcribing. Losing this CAS means Dispose won — skip.
        if (Interlocked.CompareExchange(
                ref _state,
                (int)PipelineState.Transcribing,
                (int)PipelineState.Stopping)
            != (int)PipelineState.Stopping)
        {
            DeckleWhispSource.Log.TranscribeSkipped(((PipelineState)Volatile.Read(ref _state)).ToString());
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        RaiseStatus(Loc.Get("Status_Transcribing"));

        DeckleWhispSource.Log.TranscribeCompleted(consumed.NSegments);
        DeckleWhispSource.Log.TranscribeCompleteDetail(
            consumed.TotalMs, consumed.NSegments, consumed.Text.Length);

        if (string.IsNullOrWhiteSpace(consumed.Text))
        {
            RaiseStatus(Loc.Get("Status_Ready"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        // The raw take (untouched capture) is the corpus audio, exactly as in the
        // monolithic path. No per-utterance DSP runs in the socle (see the module
        // CLAUDE.md / plan), so backend audio == raw take.
        float[] raw = capture.Pcm;

        return new PipelineProduction(
            RawText:           consumed.Text,
            RawAudio:          raw,
            BackendAudio:      raw,
            TotalTranscribeMs: consumed.TotalMs,
            InitMs:            consumed.InitMs,
            VadMs:             consumed.VadMs,
            NSegments:         consumed.NSegments);
    }

    // Drains the utterance channel, transcribing each one as it arrives and
    // accumulating the text. Runs on its own task; the only backend caller at a
    // time. drainCt aborts an in-flight call on Dispose (→ OCE, propagated).
    private async Task<StreamingConsumeResult> ConsumeUtterancesAsync(
        ChannelReader<Utterance> reader, CancellationToken drainCt)
    {
        var sb = new StringBuilder();
        long totalMs = 0, initMs = 0, vadMs = 0;
        int nSeg = 0;

        string fixedPrompt = _host.Transcription.Engine.InitialPrompt ?? "";
        string? previousTail = null;

        await foreach (Utterance u in reader.ReadAllAsync(drainCt).ConfigureAwait(false))
        {
            // Inter-utterance context, rebuilt by hand: the fixed stylistic prompt
            // plus the tail of the previous utterance, so continuity carries
            // across these separate backend calls (Whisper has no cross-call
            // context). A looped previous utterance contributes no tail, so a loop
            // cannot contaminate the next.
            var ctx = new TranscriptionContext(BuildPriming(fixedPrompt, previousTail));

            TranscriptionResult result;
            try
            {
                result = await _backend.TranscribeAsync(
                    u.Samples,
                    seg => NewSegment?.Invoke(seg),
                    drainCt,
                    ctx).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Dispose aborted us — stop draining, abandon.
                throw;
            }
            catch (Exception ex)
            {
                // One utterance failing must not lose the whole dictation — log,
                // drop its context, keep going (resilience, the first goal).
                DeckleWhispSource.Log.TranscribeFailed(-1);
                DeckleWhispSource.Log.SegmentCallbackThrew(ex.GetType().Name, ex.Message);
                previousTail = null;
                continue;
            }

            totalMs += result.TotalDurationMs;
            initMs  += result.InitDurationMs;
            vadMs   += result.VadDurationMs;
            nSeg    += result.Segments.Count;

            string text = result.FullText?.Trim() ?? "";
            if (text.Length > 0)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(text);
            }

            // Carry the tail forward unless the utterance looped (Aborted with the
            // repetition guard) — then a contaminated tail must not prime the next.
            previousTail = result.Aborted ? null : TailOf(text);
        }

        return new StreamingConsumeResult(sb.ToString(), totalMs, initMs, vadMs, nSeg);
    }

    // Fixed stylistic prompt + the previous utterance's tail (most recent context
    // last, closest to the current audio — how Whisper reads initial_prompt).
    private static string BuildPriming(string fixedPrompt, string? previousTail)
    {
        if (string.IsNullOrWhiteSpace(previousTail)) return fixedPrompt;
        if (string.IsNullOrWhiteSpace(fixedPrompt)) return previousTail;
        return fixedPrompt + " " + previousTail;
    }

    // Last N words of an utterance — the continuity carried into the next.
    // A modest, tunable window: enough for word-boundary/register continuity
    // without blowing the prompt-token budget (Whisper keeps the most recent
    // tokens if the prompt overflows anyway).
    private const int PrimingTailWords = 30;
    private static string TailOf(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string[] words = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= PrimingTailWords) return text.Trim();
        return string.Join(' ', words[^PrimingTailWords..]);
    }

    // Roll-up the consumer hands back to ProduceStreamingAsync — assembled text
    // plus the backend timings summed across utterances.
    private readonly record struct StreamingConsumeResult(
        string Text, long TotalMs, long InitMs, long VadMs, int NSegments);
}

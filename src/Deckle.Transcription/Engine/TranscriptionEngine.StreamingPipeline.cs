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
    // utterances on the worker thread (via the Audio Frame event), a Channel hands
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
    // only) aborts the consumer. On Stop the consumer is NOT cancelled, so the
    // queued tail transcribes losslessly; on Dispose it is, so the worker join
    // stays bounded. Note how the abort actually lands: the backend observes
    // drainCt through its abort hook and returns a normal result with
    // Aborted=true (it does NOT throw), so the deterministic stop comes from the
    // explicit drainCt check at the top of the consumer loop, not from an
    // exception out of the backend call.
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
        }
        catch
        {
            // Record threw. The channel is completed (finally above), so the
            // consumer drains and finishes on its own — observe it here so its
            // exception is never left unobserved, then let the original throw
            // propagate to WorkerRun's crash handler.
            try { await consumer.ConfigureAwait(false); } catch { /* teardown */ }
            throw;
        }

        _recordDrainDuration = capture.DrainDuration;

        // Cap-hit CAS BEFORE the drain await, symmetric with the monolithic path:
        // otherwise the state would stay Recording for the whole drain and a hotkey
        // arriving meanwhile would be mistaken for a valid Stop. On a user Stop the
        // CAS already happened in RequestToggle.
        if (capture.Outcome == CaptureOutcome.CapHit)
        {
            Interlocked.CompareExchange(
                ref _state, (int)PipelineState.Stopping, (int)PipelineState.Recording);
        }

        // Await the consumer drain. On Stop it finishes naturally (lossless tail);
        // on Dispose the loop's drainCt check throws OCE → abandon.
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

        // Streaming-native completion recap, emitted once at Stop: the readable
        // summary of the whole take (utterances the segmenter produced, audio
        // length, cumulative Whisper time, word count, Whisper's own sub-segments).
        double takeAudioSec = (float)capture.Pcm.Length / 16_000f;
        DeckleWhispSource.Log.StreamingDrained(
            consumed.NUtterances,
            takeAudioSec,
            consumed.TotalMs,
            TextMetrics.CountWords(consumed.Text),
            consumed.NSegments);

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
    // time. The drainCt check at the top of each iteration is what guarantees a
    // deterministic stop on Dispose — the backend returns Aborted (it does not
    // throw on cancellation), and ReadAllAsync may still yield a ready item after
    // cancellation, so we must check the token ourselves.
    private async Task<StreamingConsumeResult> ConsumeUtterancesAsync(
        ChannelReader<Utterance> reader, CancellationToken drainCt)
    {
        var sb = new StringBuilder();
        long totalMs = 0, initMs = 0, vadMs = 0;
        int nSeg = 0, nUtt = 0;

        string fixedPrompt = _host.Transcription.Engine.InitialPrompt ?? "";
        string? previousTail = null;

        await foreach (Utterance u in reader.ReadAllAsync(drainCt).ConfigureAwait(false))
        {
            // Deterministic Dispose stop — see the method remark.
            drainCt.ThrowIfCancellationRequested();
            nUtt++;

            // Inter-utterance context, rebuilt by hand: the fixed stylistic prompt
            // plus the tail of the previous utterance, so continuity carries
            // across these separate backend calls (Whisper has no cross-call
            // context). A looped previous utterance contributes no tail, so a loop
            // cannot contaminate the next.
            //
            // EmitPreamble only on the first utterance (nUtt was ++'d above, so
            // ==1 here) → the params/prompt log once for the whole take, not once
            // per utterance. TimelineOffsetSec positions this utterance's segments
            // on the take's global timeline so the logs read true positions.
            var ctx = new TranscriptionContext(
                BuildPriming(fixedPrompt, previousTail),
                EmitPreamble:      nUtt == 1,
                TimelineOffsetSec: u.StartSec);

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
                DeckleWhispSource.Log.UtteranceSkipped(u.Index, ex.GetType().Name, ex.Message);
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

        return new StreamingConsumeResult(sb.ToString(), totalMs, initMs, vadMs, nSeg, nUtt);
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
    // PrimingTailWords is a reference value, not exposed and not yet tuned: enough
    // for word-boundary/register continuity without blowing the prompt-token
    // budget (Whisper keeps the most recent tokens if the prompt overflows anyway).
    private const int PrimingTailWords = 30;
    private static string TailOf(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string[] words = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= PrimingTailWords) return text.Trim();
        return string.Join(' ', words[^PrimingTailWords..]);
    }

    // Roll-up the consumer hands back to ProduceStreamingAsync — assembled text,
    // the backend timings summed across utterances, and how many utterances the
    // segmenter produced.
    private readonly record struct StreamingConsumeResult(
        string Text, long TotalMs, long InitMs, long VadMs, int NSegments, int NUtterances);
}

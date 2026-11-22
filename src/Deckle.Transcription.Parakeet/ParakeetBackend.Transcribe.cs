using System.Diagnostics;
using System.Diagnostics.Tracing;
using Deckle.Diagnostics;
using Deckle.Transcription.Parakeet.Decoding;
using Deckle.Transcription.Parakeet.Segments;

namespace Deckle.Transcription.Parakeet;

public sealed partial class ParakeetBackend
{
    // Parakeet is a transducer with no text prompt: it has nothing that plays the role of
    // a priming text, so TranscriptionContext.PrimingText is ignored on purpose, and
    // setting it changes nothing. ContinuesPreviousCall is ignored for the same reason in the
    // other direction: the decoder keeps no text between calls (its recurrent state starts from
    // zeros at every chunk, as the reference loop does), so every call is already independent
    // and no take can inherit another's text. Only EmitPreamble and TimelineOffsetSec are used,
    // and both only for what is logged.
    public Task<TranscriptionResult> TranscribeAsync(
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct,
        TranscriptionContext? context = null)
    {
        return Task.FromResult(TranscribeSync(pcmSamples, segmentSink, ct, context));
    }

    // Across calls: closed by the first call whose segments all reach the sink.
    private bool _segmentCallbackIncidentOpen;

    private TranscriptionResult TranscribeSync(
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct,
        TranscriptionContext? context)
    {
        if (pcmSamples.Length == 0)
        {
            DeckleWhispSource.Log.TranscribeEmpty();
            return new TranscriptionResult(Array.Empty<TranscriptionSegment>(), "", 0, 0, false, 0);
        }

        lock (_transcribeLock)
        {
            // Read inside the lock: an unload waits for this call, so the model stays alive until it ends.
            if (_model is not { } model)
            {
                // The same -1 a failing inference returns: the reason tells the two apart in the log.
                DeckleParakeetSource.Log.TranscribeFailed("no_context", "none", "", "");
                return new TranscriptionResult(Array.Empty<TranscriptionSegment>(), "", 0, 0, false, -1, "no_context");
            }

            return Run(model, pcmSamples, segmentSink, ct, context);
        }
    }

    private TranscriptionResult Run(
        ParakeetLoadedModel model,
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct,
        TranscriptionContext? context)
    {
        double timelineOffsetSec = context?.TimelineOffsetSec ?? 0;
        double audioSec = (double)pcmSamples.Length / LongAudioChunker.SampleRate;
        bool detail = DetailEnabled();
        var totalSw = Stopwatch.StartNew();
        LexiconSnapshot? lexicon = TryResolveLexicon(model);

        // A standalone call and the first utterance of a streaming take log the configuration;
        // the rest skip it, since it is identical across the take. The lexicon appears as counts
        // only: its terms are the person's own words.
        if (context?.EmitPreamble ?? true)
        {
            DeckleWhispSource.Log.TranscribeStarted();
            if (detail)
            {
                DeckleWhispSource.Log.TranscribeStartDetail(audioSec, pcmSamples.Length, "greedy_tdt");
                DeckleWhispSource.Log.TranscribeParams(
                    $"engine=parakeet | accelerator={model.Accelerator} | max_tokens_per_step={TdtGreedyDecoder.DefaultMaxTokensPerStep}" +
                    $" | chunk_max_s={LongAudioChunker.MaxChunkSamples / LongAudioChunker.SampleRate}" +
                    $" | lexicon_terms={lexicon?.Terms.Count ?? 0} | boost={(lexicon?.Boost is { } b ? $"{BoostStrength:0.##}x{b.TermCount}" : "off")}");
            }
        }

        var segments = new List<TranscriptionSegment>();
        var decoder = new TdtGreedyDecoder(model.Joint, model.Vocabulary.BlankId, boost: lexicon?.Boost);
        IReadOnlyList<AudioChunk> chunks = LongAudioChunker.Split(pcmSamples.Span);

        int chunksDone = 0, totalFrames = 0, totalTokens = 0, boostedTokens = 0, sinkFailures = 0;
        long encodeMs = 0, decodeMs = 0;
        bool aborted = false;
        string step = "encode";

        int c = 0;
        bool fellBackToCpu = false;
        while (true)
        {
            try
            {
                for (; c < chunks.Count; c++)
                {
                    // Cancellation keeps what earlier chunks produced; a chunk cut short is dropped whole,
                    // since the decoder only hands its tokens back at the end.
                    if (ct.IsCancellationRequested) { aborted = true; break; }

                    AudioChunk chunk = chunks[c];
                    step = "encode";
                    var stepSw = Stopwatch.StartNew();
                    IReadOnlyList<ReadOnlyMemory<float>> frames = model.Encoder.Encode(pcmSamples.Span.Slice(chunk.Offset, chunk.Length));
                    long chunkEncodeMs = stepSw.ElapsedMilliseconds;
                    encodeMs += chunkEncodeMs;

                    // The encoder cannot be interrupted once started; this is the first place to notice.
                    if (ct.IsCancellationRequested) { aborted = true; break; }

                    step = "decode";
                    stepSw.Restart();
                    IReadOnlyList<TdtToken> tokens;
                    try
                    {
                        tokens = decoder.Decode(frames, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        aborted = true;
                        break;
                    }
                    long chunkDecodeMs = stepSw.ElapsedMilliseconds;
                    decodeMs += chunkDecodeMs;

                    // Silent ids (control tokens) carry no speech: they never reach the text.
                    var decoded = new List<DecodedToken>(tokens.Count);
                    foreach (TdtToken token in tokens)
                    {
                        if (model.Vocabulary.IsSilent(token.Id)) continue;
                        decoded.Add(new DecodedToken(token.Id, model.Vocabulary.TextOf(token.Id), token.Frame, token.Probability));
                    }

                    // The chunk's offset in centiseconds, so segments of a long take share one timeline.
                    long offsetCs = (long)chunk.Offset * 100 / LongAudioChunker.SampleRate;
                    foreach (TranscriptionSegment built in SegmentBuilder.Build(decoded, offsetCs))
                    {
                        TranscriptionSegment segment = lexicon is null ? built : built with { Text = lexicon.Recaser.Apply(built.Text) };
                        segments.Add(segment);
                        DeliverSegment(segment, segmentSink, ref sinkFailures);
                        if (detail) LogSegment(segments.Count, segment, timelineOffsetSec);
                    }

                    totalFrames += frames.Count;
                    totalTokens += tokens.Count;
                    boostedTokens += decoder.BoostedTokens;
                    chunksDone++;
                    DeckleParakeetSource.Log.ChunkDecoded(
                        c + 1, chunks.Count, (double)chunk.Offset / LongAudioChunker.SampleRate,
                        (double)chunk.Length / LongAudioChunker.SampleRate, frames.Count, tokens.Count, chunkEncodeMs, chunkDecodeMs);
                }
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The GPU opened but failed under load (out of memory, device lost): once per call,
                // the chunk that failed restarts on a CPU encoder, and the chunks already decoded
                // stay, so the sink never receives the same segment twice.
                if (!fellBackToCpu && !ct.IsCancellationRequested
                    && model.Accelerator == ParakeetAccelerators.DirectMl
                    && TryFallBackToCpu(model, step, ex) is { } cpuModel)
                {
                    fellBackToCpu = true;
                    model = cpuModel;
                    decoder = new TdtGreedyDecoder(model.Joint, model.Vocabulary.BlankId, boost: lexicon?.Boost);
                    continue;
                }

                // An ONNX or decoding failure: the segments so far are dropped with the call, so a
                // truncated text never passes for a whole one. The engine's failure event is the
                // visible incident and carries the reason; this line carries the cause behind it.
                DeckleParakeetSource.Log.TranscribeFailed("inference_failed", step, ex.GetType().Name, ex.Message);
                return new TranscriptionResult(
                    Array.Empty<TranscriptionSegment>(), "", totalSw.ElapsedMilliseconds, 0, false, -1, "inference_failed");
            }
        }

        if (_segmentCallbackIncidentOpen && sinkFailures == 0 && segments.Count > 0)
        {
            _segmentCallbackIncidentOpen = false;
            DeckleParakeetSource.Log.SegmentCallbackRecovered();
        }

        long totalMs = totalSw.ElapsedMilliseconds;
        string fullText = string.Join(" ", segments.Select(s => s.Text)).Trim();

        // Every call, not only the first: what this call decoded with, and where its last segment
        // ended against the audio it was given. The table is the engine contract's, so a take reads
        // the same whichever engine ran it; the prompt and temperature columns are zero because
        // Parakeet has neither.
        DeckleWhispSource.Log.TranscribeDecodeUsed(
            timelineOffsetSec, 0, false, true, 0, 0,
            segments.Count,
            segments.Count > 0 ? segments[^1].T1Cs / 100.0 : 0.0,
            audioSec,
            aborted);
        DeckleParakeetSource.Log.TranscribeSummary(
            audioSec, chunks.Count, chunksDone, totalFrames, totalTokens, segments.Count, encodeMs, decodeMs, aborted, boostedTokens);

        return new TranscriptionResult(segments, fullText, totalMs, 0, aborted, 0);
    }

    // Called under _transcribeLock, so no call is using the failed model: it is replaced by one whose
    // encoder runs on the CPU, and the next takes use that one until the next load. _modelLock is not
    // taken here: an unload takes it before _transcribeLock, and the reverse order would deadlock.
    // Null when the CPU model cannot be built; the original failure then stands.
    private ParakeetLoadedModel? TryFallBackToCpu(ParakeetLoadedModel failed, string step, Exception cause)
    {
        EngineSettings engine = _host.Transcription.Engine;
        ParakeetModelFiles files = ParakeetModelFiles.Resolve(engine.ParakeetModelDirectory, _host.ResolveModelsDirectory());
        ParakeetLoadOutcome outcome = _loader.Load(files, engine.ParakeetEncoder, useGpu: false);
        if (outcome.Model is not { } cpu) return null;

        DeckleParakeetSource.Log.TranscribeGpuFallback(step, cause.GetType().Name, PrecisionName(engine.ParakeetEncoder));
        _model = cpu;
        failed.Dispose();
        return cpu;
    }

    // The sink runs on this thread; one that throws must not lose the rest of the call.
    private void DeliverSegment(TranscriptionSegment segment, Action<TranscriptionSegment>? sink, ref int failures)
    {
        if (sink is null) return;
        try
        {
            sink(segment);
        }
        catch (Exception ex)
        {
            // A sink that throws once usually throws at every take: one Error per incident, the
            // raw failures in the gated detail.
            failures++;
            if (!_segmentCallbackIncidentOpen)
            {
                _segmentCallbackIncidentOpen = true;
                DeckleParakeetSource.Log.SegmentCallbackFailed(ex.GetType().Name, ex.Message);
            }
            DeckleParakeetSource.Log.SegmentCallbackFailedDetail(failures, ex.GetType().Name, ex.Message);
        }
    }

    private static bool DetailEnabled() =>
        OperationalLogAdmission.IsDetailEnabled(
            OperationalLogActivity.Transcription,
            DeckleWhispSource.Log,
            EventLevel.Verbose,
            (EventKeywords)Keywords.Pipeline);

    private static void LogSegment(int index, TranscriptionSegment segment, double timelineOffsetSec)
    {
        double t0 = timelineOffsetSec + segment.T0Cs / 100.0;
        double t1 = timelineOffsetSec + segment.T1Cs / 100.0;
        DeckleWhispSource.Log.SegmentRecognized(
            index, t0, t1, (segment.T1Cs - segment.T0Cs) / 100.0,
            segment.NoSpeechProb, segment.Confidence, segment.MinTokenProbability,
            segment.TextTokens, segment.TextTokens, segment.Text.Trim().Length);
    }
}

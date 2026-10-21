using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text;
using System.Threading.Channels;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Transcription;
using Deckle.Vad;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── StreamingPipeline partial — producer/consumer over utterances ──────────
    //
    // An energy segmenter cuts the live capture stream into utterances on the
    // worker thread (via the capture Frame event); a Channel hands them to a
    // consumer task that transcribes each one as it arrives. At Stop the
    // producer flushes the open utterance and completes the channel; the
    // consumer drains the queue, then the text flows into FinalizeTranscription.
    // The perceived Stop latency is only the remaining backlog, and a decoder
    // loop is confined to one utterance instead of poisoning the whole take.
    //
    // Threading: capture (Record) blocks the worker thread, which is the
    // producer, so the segmenter is single-threaded. The consumer is the only
    // backend caller at a time (sequential loop), so the single-context
    // invariant holds. The Channel is the one thread-safe hand-off.
    //
    // Two tokens: producerCt (Stop + Dispose) stops capture; drainCt (Dispose
    // only) aborts the consumer, so on Stop the queued tail transcribes
    // losslessly. The backend observes drainCt through its abort hook and
    // returns Aborted=true without throwing, so the deterministic stop is the
    // explicit drainCt check at the top of the consumer loop.
    private async Task<PipelineProduction?> ProduceStreamingAsync(
        CancellationToken producerCt, CancellationToken drainCt, Task<bool> primeTask)
    {
        // Opt-in batch rewriting (TranscriptionEngine.BatchRewrite.cs). The feed
        // is subscribed before the consumer starts and disposed when this method
        // exits, which every exit path reaches only after the consumer drain has
        // ended, so no utterance is missed and the subscription never outlives
        // the take. On an abandoned production (a null return) the batches
        // already sent finish on their own and their results are dropped with
        // the rewriter.
        BatchRewriteRun? batchRewrite = StartBatchRewrite();
        using IDisposable? batchFeed = FeedBatchRewrite(batchRewrite);

        // Crash-safety copy of the raw text (TranscriptionEngine.Recovery.cs),
        // with the same lifetime as the batch feed.
        using IDisposable? recoveryFeed = FeedRecovery();

        var policy = new SegmentedTranscriptionPolicy(
            FixedPrompt: _host.Transcription.Engine.InitialPrompt ?? "",
            ApplyPreprocessing: true,
            RejectIncompleteResults: false);
        SegmentedTranscriptionSession<StreamingConsumeResult> session =
            StartSegmentedTranscription(policy, drainCt, primeTask);

        // 1 Hz heartbeat: 20 × 50 ms frames per tick. recordSw starts on the
        // first frame the heartbeat admits and feeds the recording_sec field.
        Stopwatch? recordSw = null;
        int hbFrames = 0;
        float lastRmsLinear = 0f;

        void OnFrame(CaptureFrame f)
        {
            session.Push(f);
            UtteranceProgressChanged?.Invoke(session.Progress);

            if (!OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Transcription,
                    DeckleWhispSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Heartbeat))
            {
                return;
            }

            recordSw ??= Stopwatch.StartNew();
            lastRmsLinear = f.Rms;
            if (++hbFrames >= 20)
            {
                hbFrames = 0;
                var snap = session.Snapshot();
                double rmsDbfs = lastRmsLinear > 1e-6f
                    ? 20.0 * Math.Log10(lastRmsLinear)
                    : -120.0;
                DeckleWhispSource.Log.StreamingHeartbeat(
                    snap.State,
                    rmsDbfs,
                    snap.CurrentUtteranceFrames * 50,
                    snap.RequiredHangoverFrames * 50,
                    session.Backlog,
                    snap.TotalUtterancesEmitted,
                    (int)(recordSw.ElapsedMilliseconds / 1000));
            }
        }

        // The consumer must be live BEFORE capture starts — frames (and the
        // utterances they yield) arrive during Record. It crosses the prime gate
        // before its first backend call; utterances queue in the unbounded channel
        // meanwhile, so nothing is lost while the model warms.
        UtteranceProgressChanged?.Invoke(session.Progress);
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
                try { session.Complete(); }
                finally { UtteranceProgressChanged?.Invoke(default); }
            }
        }
        catch
        {
            // Record threw. The channel is completed (finally above), so the
            // consumer drains and finishes on its own — observe it here so its
            // exception is never left unobserved, then let the original throw
            // propagate to WorkerRun's crash handler.
            try { await session.Completion.ConfigureAwait(false); }
            catch (Exception teardownEx)
            {
                // The Record exception is the one that propagates; this one is secondary.
                DeckleWhispSource.Log.TeardownExceptionIgnored(
                    "streaming_capture_teardown", teardownEx.GetType().Name, teardownEx.Message);
            }
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
            consumed = await session.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DeckleWhispSource.Log.TranscribeSkipped("disposed");
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        // The perceived Stop→drained latency. On a cap hit the stopwatch was
        // never started, so it reports 0.
        if (_stopToPipelineSw is { IsRunning: true }) _stopToPipelineSw.Stop();

        if (capture.Outcome == CaptureOutcome.MicError)
        {
            var (title, body) = LocalizeMicError(MicErrorKind.Unavailable, capture.MmsysErr);
            OpenMicrophoneIncident("capture", capture.MmsysErr);
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

        DeckleWhispSource.Log.StreamingDrained();
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription,
                DeckleWhispSource.Log,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Pipeline))
        {
            double takeAudioSec = (float)capture.Pcm.Length / 16_000f;
            DeckleWhispSource.Log.StreamingDrainedDetail(
                consumed.NUtterances,
                takeAudioSec,
                consumed.TotalMs,
                TextMetrics.CountWords(consumed.Text),
                consumed.NSegments);
        }

        if (string.IsNullOrWhiteSpace(consumed.Text))
        {
            RaiseStatus(Loc.Get("Status_Ready"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        // RawAudio is the untouched capture, the normalized corpus baseline. When
        // the DSP or the VAD trim ran, the consumer handed back the concatenation
        // of what the backend received (the kept utterances, not the continuous
        // take); otherwise BackendAudio falls back to raw.
        float[] raw = capture.Pcm;
        float[] backendAudio = consumed.BackendAudio ?? raw;

        if (consumed.BackendAudio is not null) EmitPreprocessedTelemetry(consumed.BackendAudio);

        // Sends the last open batch now rather than at Assemble, so its request
        // runs while the finalize copies the raw text. Only here, on the path
        // that finalizes: an abandoned take must not send a request whose
        // result nobody reads.
        batchRewrite?.Rewriter.Complete();

        return new PipelineProduction(
            RawText:           consumed.Text,
            RawAudio:          raw,
            BackendAudio:      backendAudio,
            TotalTranscribeMs: consumed.TotalMs,
            InitMs:            consumed.InitMs,
            NSegments:         consumed.NSegments,
            BatchRewrite:      batchRewrite);
    }

    // Creates the source-neutral segmenter/channel/consumer core shared by live
    // capture and decoded files. The caller owns only the audio producer and the
    // engine lifecycle around this session.
    private SegmentedTranscriptionSession<StreamingConsumeResult> StartSegmentedTranscription(
        SegmentedTranscriptionPolicy policy,
        CancellationToken drainCt,
        Task<bool> primeTask)
    {
        var segCfg = _host.Transcription.Streaming.Segmenter;
        DeckleWhispSource.Log.StreamingPipelineStarted();
        DeckleWhispSource.Log.SegmenterSettingsSnapshot(
            segCfg.ThresholdDbfs,
            segCfg.HangoverMaxMs, segCfg.HangoverMinMs,
            segCfg.HangoverRampStartMs, segCfg.HangoverRampEndMs,
            segCfg.HangoverCurveX1, segCfg.HangoverCurveY1,
            segCfg.HangoverCurveX2, segCfg.HangoverCurveY2,
            segCfg.MarginMs, segCfg.MinUtteranceMs);

        var trimCfg = _host.Transcription.Streaming.SpeechTrim;
        SileroVad? vad = trimCfg.Enabled ? _vadService.EnsureReady() : null;
        if (trimCfg.Enabled && vad is null)
            DeckleVadSource.Log.SpeechTrimNotReady();

        var vadOptions = new SileroVadOptions
        {
            Threshold            = trimCfg.Threshold,
            MinSpeechDurationMs  = trimCfg.MinSpeechDurationMs,
            MinSilenceDurationMs = trimCfg.MinSilenceDurationMs,
            SpeechPadMs          = trimCfg.SpeechPadMs,
        };
        if (vad is not null)
            DeckleVadSource.Log.SpeechTrimSettingsSnapshot(
                vadOptions.Threshold, vadOptions.MinSpeechDurationMs,
                vadOptions.MinSilenceDurationMs, vadOptions.SpeechPadMs);

        return new SegmentedTranscriptionSession<StreamingConsumeResult>(
            segCfg,
            (reader, onDequeue) => Task.Run(() => ConsumeUtterancesAsync(
                reader,
                drainCt,
                onDequeue,
                vad,
                vadOptions,
                primeTask,
                policy)));
    }

    // Drains the utterance channel, transcribing each one as it arrives and
    // accumulating the text. Runs on its own task; the only backend caller at a
    // time. The drainCt check at the top of each iteration is what guarantees a
    // deterministic stop on Dispose — the backend returns Aborted (it does not
    // throw on cancellation), and ReadAllAsync may still yield a ready item after
    // cancellation, so we must check the token ourselves.
    private async Task<StreamingConsumeResult> ConsumeUtterancesAsync(
        ChannelReader<Utterance> reader, CancellationToken drainCt, Func<int> onDequeue,
        SileroVad? vad, SileroVadOptions vadOptions, Task<bool> primeTask,
        SegmentedTranscriptionPolicy policy)
    {
        // Prime gate. The producer keeps capturing and segmenting meanwhile; the
        // unbounded channel buffers the backlog, so the wait hides behind the
        // recording instead of adding to the Stop latency.
        if (!await AwaitPrime(primeTask, "during_recording").ConfigureAwait(false))
        {
            // Prime failed (UserFeedback already shown), but Record is still
            // capturing and the segmenter keeps writing utterances. Drain and
            // discard so those buffers are released as they arrive instead of
            // piling up until Stop; the empty result takes the caller's
            // empty-text exit. Dispose (drainCt) ends the drain early.
            int discarded = 0;
            try
            {
                await foreach (var _ in reader.ReadAllAsync(drainCt).ConfigureAwait(false)) discarded++;
            }
            catch (OperationCanceledException) { /* Dispose — abandon the discard loop */ }
            // Speech was captured and thrown away; the count says how much.
            if (discarded > 0)
                DeckleWhispSource.Log.UtterancesDiscarded("prime_failed", discarded);
            return new StreamingConsumeResult("", 0, 0, 0, 0, null, Incomplete: true);
        }

        var sb = new StringBuilder();
        long totalMs = 0, initMs = 0;
        int nSeg = 0, nUtt = 0;

        // A silence cut is a real pause the speaker made — render it as a
        // paragraph break in the assembled text instead of flattening it into a
        // space. Pending until the NEXT utterance that contributes text, so a
        // dropped or empty utterance in between does not swallow the break.
        bool pendingParagraphBreak = false;

        string fixedPrompt = policy.FixedPrompt;
        string? previousTail = null;
        bool incomplete = false;
        // Consecutive failed utterances: one Warning opens the incident, repeats
        // stay Verbose, and the next success closes it.
        int skipped = 0;

        // The opt-in DSP runs per utterance here, not once over the whole take:
        // each utterance is decoded in its own backend call, so landing each on
        // TargetRmsDbfs is the per-call level Whisper wants, and a whole-take
        // measure could only complete at Stop, defeating the streaming latency.
        // The segmenter emits only voiced spans, so boosting silence is not a
        // risk here.
        var pp = _host.Audio.Preprocessing;
        bool applyPreprocessing = policy.ApplyPreprocessing && pp.Enabled;
        // Kept whenever a stage transforms the audio (the DSP or the VAD trim),
        // so BackendAudio mirrors exactly what the backend received.
        List<float[]>? backendChunks = (applyPreprocessing || vad is not null) ? new List<float[]>() : null;

        await foreach (Utterance u in reader.ReadAllAsync(drainCt).ConfigureAwait(false))
        {
            // Deterministic Dispose stop — see the method remark.
            drainCt.ThrowIfCancellationRequested();
            nUtt++;
            // Decrement now, so heartbeat readings count the in-flight utterance
            // as dequeued whether or not the backend call succeeds.
            int backlogAfter = onDequeue();

            // Continuity is given by hand: the fixed stylistic prompt plus the
            // previous utterance's tail. A looped previous utterance contributes
            // no tail, so a loop cannot contaminate the next.
            // The backend may also keep what it decoded last (Whisper does, in its
            // decoder state). That carry follows the tail: the call continues the
            // previous one only when the tail of a call of this take is carried.
            // So the first call of the take starts clean of the warm-up's output
            // and of the previous take, and a call after a loop, a failure or an
            // empty utterance starts clean of what it decoded.
            var ctx = new TranscriptionContext(
                BuildPriming(fixedPrompt, previousTail),
                EmitPreamble:          nUtt == 1,
                TimelineOffsetSec:     u.StartSec,
                ContinuesPreviousCall: !string.IsNullOrEmpty(previousTail));

            float[] samples = u.Samples;
            if (applyPreprocessing)
            {
                var processed = TranscriptionPreprocessor.Process(u.Samples, pp);
                samples = processed.Pcm;
                DeckleWhispSource.Log.TranscriptionPreprocessed(
                    processed.InputRmsDbfs, processed.OutputRmsDbfs, processed.MakeupGainDb, processed.OutputPeak);
            }
            if (vad is not null)
            {
                try
                {
                    long trimStart = Stopwatch.GetTimestamp();
                    SpeechTrimResult trim = vad.Trim(samples, vadOptions);
                    long trimMs = (long)Stopwatch.GetElapsedTime(trimStart).TotalMilliseconds;
                    DeckleVadSource.Log.SpeechTrimmed(
                        u.Index, samples.Length, trim.Samples.Length, trim.SpeechSegments, trimMs);
                    // A trim that ran, with or without speech found, closes an
                    // earlier take's trim-failure incident.
                    _vadService.ReportTrimSucceeded();
                    if (trim.Samples.Length == 0)
                    {
                        // No speech — an energy false positive on noise/silence. Drop it
                        // rather than feed near-silence to whisper, which hallucinates on
                        // it. The previous tail is left intact: a dropped utterance adds
                        // no text and must not sever the continuity of the real ones.
                        DeckleVadSource.Log.UtteranceDroppedNoSpeech(u.Index);
                        continue;
                    }
                    samples = trim.Samples;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A VAD runtime failure must not lose the dictation: stop trimming
                    // for the rest of this take, and let this utterance through
                    // untrimmed rather than dropping it or crashing the consumer. The
                    // service keeps failures across takes as one incident, closed by
                    // the next trim that succeeds.
                    _vadService.ReportTrimFailure(u.Index, ex);
                    vad = null;
                }
            }
            backendChunks?.Add(samples);

            TranscriptionResult result;
            try
            {
                result = await _backend.TranscribeAsync(
                    samples,
                    seg => NewSegment?.Invoke(seg),
                    drainCt,
                    ctx).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One utterance failing must not lose the whole dictation: log,
                // drop its context, keep going.
                if (skipped++ == 0)
                    DeckleWhispSource.Log.UtteranceSkipped(u.Index, ex.GetType().Name, ex.Message);
                else
                    DeckleWhispSource.Log.UtteranceSkippedRepeat(u.Index, ex.GetType().Name, ex.Message);
                if (policy.RejectIncompleteResults)
                    incomplete = true;
                previousTail = null;
                continue;
            }

            // The backend reports a native failure as a result code, not an
            // exception, and logs it as Verbose detail only: the utterance joins
            // the same skipped-utterance incident, with the backend's reason.
            if (result.ResultCode != 0 && !result.Aborted)
            {
                string cause = $"{result.ErrorReason ?? "native_error"} result_code={result.ResultCode}";
                if (skipped++ == 0)
                    DeckleWhispSource.Log.UtteranceSkipped(u.Index, "", cause);
                else
                    DeckleWhispSource.Log.UtteranceSkippedRepeat(u.Index, "", cause);
                if (policy.RejectIncompleteResults)
                    incomplete = true;
                previousTail = null;
                continue;
            }

            if (skipped > 0)
            {
                DeckleWhispSource.Log.UtterancesRecovered(skipped);
                skipped = 0;
            }

            totalMs += result.TotalDurationMs;
            initMs  += result.InitDurationMs;
            nSeg    += result.Segments.Count;

            if (policy.RejectIncompleteResults && !IsFileTranscriptionResultUsable(result))
            {
                incomplete = true;
                previousTail = null;
                DeckleWhispSource.Log.ConsumerUtterance(
                    u.Index, result.TotalDurationMs, 0,
                    result.Segments.Count, backlogAfter, result.Aborted);
                continue;
            }

            string text = result.FullText?.Trim() ?? "";
            if (text.Length > 0)
            {
                if (sb.Length > 0) sb.Append(pendingParagraphBreak ? "\n\n" : " ");
                sb.Append(text);
                pendingParagraphBreak = u.EndedOnSilence;
                UtteranceTranscribed?.Invoke(new TranscribedUtterance(u.Index, text, u.EndedOnSilence));
            }

            DeckleWhispSource.Log.ConsumerUtterance(
                u.Index, result.TotalDurationMs, TextMetrics.CountWords(text),
                result.Segments.Count, backlogAfter, result.Aborted);

            // Carry the tail forward unless the utterance looped (Aborted with the
            // repetition guard) — then a contaminated tail must not prime the next.
            previousTail = result.Aborted ? null : TailOf(text);
        }

        float[]? backendAudio = backendChunks is null ? null : ConcatSamples(backendChunks);
        return new StreamingConsumeResult(
            sb.ToString(), totalMs, initMs, nSeg, nUtt, backendAudio, incomplete);
    }

    // internal for Deckle.Transcription.Tests: the ordered concatenation is what
    // makes BackendAudio a faithful normalized corpus record.
    internal static float[] ConcatSamples(List<float[]> chunks)
    {
        int total = 0;
        for (int i = 0; i < chunks.Count; i++) total += chunks[i].Length;

        var flat = new float[total];
        int offset = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            Array.Copy(chunks[i], 0, flat, offset, chunks[i].Length);
            offset += chunks[i].Length;
        }
        return flat;
    }

    // Fixed stylistic prompt + the previous utterance's tail (most recent context
    // last, closest to the current audio — how Whisper reads initial_prompt).
    private static string BuildPriming(string fixedPrompt, string? previousTail)
    {
        if (string.IsNullOrWhiteSpace(previousTail)) return fixedPrompt;
        if (string.IsNullOrWhiteSpace(fixedPrompt)) return previousTail;
        return fixedPrompt + " " + previousTail;
    }

    // Enough for word-boundary and register continuity without blowing the
    // prompt-token budget (Whisper keeps the most recent tokens if the prompt
    // overflows anyway).
    private const int PrimingTailWords = 30;
    private static string TailOf(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string[] words = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= PrimingTailWords) return text.Trim();
        return string.Join(' ', words[^PrimingTailWords..]);
    }

    // BackendAudio is null when no stage (DSP or VAD trim) transformed the
    // audio, so the caller falls back to the raw take.
    private readonly record struct StreamingConsumeResult(
        string Text, long TotalMs, long InitMs, int NSegments, int NUtterances,
        float[]? BackendAudio, bool Incomplete);

    private readonly record struct SegmentedTranscriptionPolicy(
        string FixedPrompt,
        bool ApplyPreprocessing,
        bool RejectIncompleteResults);

    internal static bool IsFileTranscriptionResultUsable(TranscriptionResult result) =>
        !result.Aborted && result.ResultCode == 0;
}

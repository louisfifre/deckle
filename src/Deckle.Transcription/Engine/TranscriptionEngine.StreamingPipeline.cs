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
        BatchRewriteTake? batchTake = StartBatchRewrite();
        using IDisposable? batchFeed = FeedBatchRewrite(batchTake);

        // Crash-safety copy of the raw text (TranscriptionEngine.Recovery.cs),
        // with the same lifetime as the batch feed.
        using IDisposable? recoveryFeed = FeedRecovery();

        var policy = new SegmentedTranscriptionPolicy(
            FixedPrompt: _host.Transcription.Engine.InitialPrompt ?? "",
            ApplyPreprocessing: true,
            RejectIncompleteResults: false,
            StopCaptureOnPrimeFailure: true,
            DeliveredText: batchTake is null ? null : batchTake.Snapshot);
        SegmentedTranscriptionSession<StreamingConsumeResult> session =
            StartSegmentedTranscription(policy, drainCt, primeTask);

        // 1 Hz heartbeat: 20 × 50 ms frames per tick. recordSw starts on the
        // first frame the heartbeat admits and feeds the recording_sec field.
        // The tick is counted whether or not the detail is admitted (see below).
        Stopwatch? recordSw = null;
        int hbFrames = 0;

        // The level fields summarise the 20 frames of the tick rather than
        // sample the last one, which could fall between two words: the energy
        // mean (squares summed, then re-rooted, the way RMS values combine)
        // and the loudest frame. The frame-by-frame curve belongs to the
        // microphone telemetry dataset, never to the log.
        double hbSumSquares = 0;
        float hbMaxRms = 0f;
        int hbLevelFrames = 0;

        static double ToDbfs(double rms) => rms > 1e-6 ? 20.0 * Math.Log10(rms) : -120.0;

        void OnFrame(CaptureFrame f)
        {
            session.Push(f);
            UtteranceProgressChanged?.Invoke(session.Progress);

            // The heartbeat is counted before the admission gate: the stall watch
            // of the loop reads this count, and a watch that only worked while the
            // Verbose detail was on would be blind exactly when nobody looks.
            bool heartbeat = ++hbFrames >= 20;
            if (heartbeat)
            {
                hbFrames = 0;
                HotLoops.WhispStreamingHeartbeat.Tick();
            }

            if (!OperationalLogAdmission.IsSummaryEnabled(
                    OperationalLogActivity.Transcription,
                    DeckleWhispSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Heartbeat))
            {
                return;
            }

            recordSw ??= Stopwatch.StartNew();
            hbSumSquares += (double)f.Rms * f.Rms;
            if (f.Rms > hbMaxRms) hbMaxRms = f.Rms;
            hbLevelFrames++;
            if (heartbeat)
            {
                var snap = session.Snapshot();
                double rmsDbfs = ToDbfs(Math.Sqrt(hbSumSquares / hbLevelFrames));
                double maxDbfs = ToDbfs(hbMaxRms);
                hbSumSquares = 0;
                hbMaxRms = 0f;
                hbLevelFrames = 0;
                DeckleWhispSource.Log.StreamingHeartbeat(
                    snap.State,
                    rmsDbfs,
                    maxDbfs,
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

        // Frames are expected for as long as Record runs, no longer: the drain
        // that follows has no frames, and its silence is not a stall.
        IDisposable heartbeatArmed = HotLoops.WhispStreamingHeartbeat.Arm();

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
                heartbeatArmed.Dispose();
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
            FinishTake(TranscriptionOutcome.None, TakeEndReasons.StateLost);
            return null;
        }

        // The perceived Stop→drained latency. On a cap hit the stopwatch was
        // never started, so it reports 0.
        if (_stopToPipelineSw is { IsRunning: true }) _stopToPipelineSw.Stop();

        if (capture.Outcome == CaptureOutcome.MicError)
        {
            var (title, body) = LocalizeMicError(MicErrorKind.Unavailable, capture.ErrorCode);
            OpenMicrophoneIncident("capture", capture.ErrorCode);
            EmitUserFeedback(FB_ERROR, title, body, FB_REPLACEMENT);
            FinishTake(TranscriptionOutcome.None, TakeEndReasons.MicrophoneError);
            return null;
        }

        // The capture ended because the engine stopped it (the prime failed), or
        // a user Stop landed before the failure was known. Either way the
        // consumer discarded everything: tell the person once, here, where the
        // capture is over and the HUD's Recording state has nothing left to say.
        // A failed model load told them already. The same exit as MicError.
        if (Volatile.Read(ref _selfStopReason) is { } selfStopReason)
        {
            if (selfStopReason == TakeEndReasons.TranscriptionUnavailable)
                EmitTranscriptionUnavailable();
            FinishTake(TranscriptionOutcome.None, selfStopReason);
            return null;
        }

        // Stopping → Transcribing. Losing this CAS means Dispose won — skip.
        if (Interlocked.CompareExchange(
                ref _state,
                (int)PipelineState.Transcribing,
                (int)PipelineState.Stopping)
            != (int)PipelineState.Stopping)
        {
            DeckleWhispSource.Log.TranscribeSkipped(((PipelineState)Volatile.Read(ref _state)).ToString());
            FinishTake(TranscriptionOutcome.None, TakeEndReasons.StateLost);
            return null;
        }

        RaiseStatus(StatusPhase.Transcribing, Loc.Get("Status_Transcribing"));

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
            RaiseStatus(StatusPhase.Ready, Loc.Get("Status_Ready"));
            // Incomplete with no text is a lost take, not a silent one: speech was
            // recorded and nothing came out of it. Either the prime failed (the
            // consumer then counted no utterance) or every utterance sent to the
            // backend was skipped. Its audio is kept for the next launch.
            string reason = !consumed.Incomplete ? TakeEndReasons.NoText
                : consumed.NUtterances == 0 ? TakeEndReasons.ModelNotReady
                : TakeEndReasons.TranscriptionFailed;
            FinishTake(TranscriptionOutcome.None, reason);
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
        //
        // A run opened at a length step is checked against the final length
        // first: when the take belongs to another profile the run is dropped
        // here, and the finalize rewrites the whole take as it always did.
        BatchRewriteRun? batchRewrite = batchTake?.RunForFinalize(consumed.Text.Length);
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
        double voiceLevelDbfs = _host.Audio.VoiceLevelDbfs;
        int minSpeechMs = _host.Transcription.Streaming.MinSpeechMs;
        DeckleWhispSource.Log.StreamingPipelineStarted();
        DeckleWhispSource.Log.SegmenterSettingsSnapshot(
            voiceLevelDbfs,
            segCfg.HangoverMaxMs, segCfg.HangoverMinMs,
            segCfg.HangoverRampStartMs, segCfg.HangoverRampEndMs,
            segCfg.HangoverCurveX1, segCfg.HangoverCurveY1,
            segCfg.HangoverCurveX2, segCfg.HangoverCurveY2,
            segCfg.MarginMs, minSpeechMs);

        (SileroVad? vad, SileroVadOptions vadOptions) = PrepareSpeechTrim();

        return new SegmentedTranscriptionSession<StreamingConsumeResult>(
            segCfg,
            voiceLevelDbfs,
            minSpeechMs,
            (reader, onDequeue) => Task.Run(() => ConsumeUtterancesAsync(
                reader,
                drainCt,
                onDequeue,
                vad,
                vadOptions,
                primeTask,
                policy)));
    }

    // The Silero trim of one take, shared by the segmented session and the
    // monolithic take: the VAD when the trim is on and the model is ready (null
    // otherwise, the take then runs untrimmed), and the options built from the
    // settings, with the shared minimum speech as Silero's span floor.
    private (SileroVad? Vad, SileroVadOptions Options) PrepareSpeechTrim()
    {
        var trimCfg = _host.Transcription.Streaming.SpeechTrim;
        SileroVad? vad = trimCfg.Enabled ? _vadService.EnsureReady() : null;
        if (trimCfg.Enabled && vad is null)
            DeckleVadSource.Log.SpeechTrimNotReady();

        var vadOptions = new SileroVadOptions
        {
            Threshold            = trimCfg.Threshold,
            MinSpeechDurationMs  = _host.Transcription.Streaming.MinSpeechMs,
            MinSilenceDurationMs = trimCfg.MinSilenceDurationMs,
            SpeechPadMs          = trimCfg.SpeechPadMs,
        };
        if (vad is not null)
            DeckleVadSource.Log.SpeechTrimSettingsSnapshot(
                vadOptions.Threshold, vadOptions.MinSpeechDurationMs,
                vadOptions.MinSilenceDurationMs, vadOptions.SpeechPadMs);

        return (vad, vadOptions);
    }

    // Trims one buffer to its speech spans. Returns the kept samples, empty when
    // Silero found no speech, or null when the trim failed: the failure is then
    // reported to the service, which keeps failures across takes as one
    // incident closed by the next trim that succeeds, and the caller goes on
    // with the untrimmed audio. utteranceIndex only labels the log lines.
    private float[]? TrimSpeech(SileroVad vad, SileroVadOptions vadOptions, float[] samples, int utteranceIndex)
    {
        try
        {
            long trimStart = Stopwatch.GetTimestamp();
            SpeechTrimResult trim = vad.Trim(samples, vadOptions);
            long trimMs = (long)Stopwatch.GetElapsedTime(trimStart).TotalMilliseconds;
            DeckleVadSource.Log.SpeechTrimmed(
                utteranceIndex, samples.Length, trim.Samples.Length, trim.SpeechSegments, trimMs);
            if (trim.SpeechSegments > 0)
            {
                int inMs = samples.Length / 16;
                int keptMs = trim.Samples.Length / 16;
                int leadingMs = trim.FirstSpeechSample / 16;
                int trailingMs = (samples.Length - trim.LastSpeechSample) / 16;
                DeckleVadSource.Log.SpeechTrimBounds(
                    utteranceIndex, inMs, keptMs, leadingMs, trailingMs,
                    Math.Max(0, inMs - keptMs - leadingMs - trailingMs));
            }
            // A trim that ran, with or without speech found, closes an earlier
            // take's trim-failure incident.
            _vadService.ReportTrimSucceeded();
            return trim.Samples;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _vadService.ReportTrimFailure(utteranceIndex, ex);
            return null;
        }
    }

    // Trims one buffer at its edges only: the samples from the first speech
    // span's start to the last one's end, pauses in between kept as spoken.
    // The monolithic take uses it, where TrimSpeech would glue every pause of a
    // long dictation together and its per-span floor would lose a short word
    // said after a pause; the edge detection also lowers that floor to one
    // frame, since a short word can be the first or last of the take (the
    // slicing and the floor are SpeechEdges, in Deckle.Vad, tested there).
    // Returns the slice, empty when Silero found no speech,
    // or null when the detection failed, reported to the service as TrimSpeech
    // does. The log lines are TrimSpeech's: kept is the slice's length, so the
    // inner-gap figure of SpeechTrimBounds reads 0.
    private float[]? TrimSpeechEdges(SileroVad vad, SileroVadOptions vadOptions, float[] samples, int utteranceIndex)
    {
        try
        {
            long trimStart = Stopwatch.GetTimestamp();
            // The span floor is lowered to one frame (SpeechEdges): at a take's
            // edge a span under the shared floor is a short word, not a blip.
            IReadOnlyList<SpeechSegment> segments =
                vad.DetectSpeech(samples, SpeechEdges.OptionsFor(vadOptions));
            float[] kept = SpeechEdges.Slice(samples, segments);
            long trimMs = (long)Stopwatch.GetElapsedTime(trimStart).TotalMilliseconds;
            DeckleVadSource.Log.SpeechTrimmed(
                utteranceIndex, samples.Length, kept.Length, segments.Count, trimMs);
            if (segments.Count > 0)
            {
                int inMs = samples.Length / 16;
                int keptMs = kept.Length / 16;
                int leadingMs = segments[0].StartSample / 16;
                int trailingMs = (samples.Length - segments[^1].EndSample) / 16;
                DeckleVadSource.Log.SpeechTrimBounds(
                    utteranceIndex, inMs, keptMs, leadingMs, trailingMs,
                    Math.Max(0, inMs - keptMs - leadingMs - trailingMs));
            }
            _vadService.ReportTrimSucceeded();
            return kept;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _vadService.ReportTrimFailure(utteranceIndex, ex);
            return null;
        }
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
            // The prime failed or was cancelled. Whether the person was told
            // depends on why (see PrimeFailure), and the pipeline decides that
            // once the capture has ended. Record is still capturing and the
            // segmenter keeps writing utterances: a failed live take ends its
            // own capture rather than record for nothing, and the discard below
            // releases the buffers as they arrive instead of letting them pile
            // up until the capture returns. The empty result takes the caller's
            // exit. Dispose (drainCt) ends the drain early.
            if (policy.StopCaptureOnPrimeFailure)
                StopLiveTakeAfterPrimeFailure();
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
            // The full trim, to the speech spans concatenated: an utterance is
            // short and already cut on a pause, so gluing its spans costs nothing.
            // The monolithic take trims its edges only (TrimSpeechEdges).
            if (vad is not null)
            {
                float[]? trimmed = TrimSpeech(vad, vadOptions, samples, u.Index);
                if (trimmed is null)
                {
                    // A VAD runtime failure must not lose the dictation: stop trimming
                    // for the rest of this take, and let this utterance through
                    // untrimmed rather than dropping it or crashing the consumer.
                    vad = null;
                }
                else if (trimmed.Length == 0)
                {
                    // No speech — an energy false positive on noise/silence. Drop it
                    // rather than feed near-silence to whisper, which hallucinates on
                    // it. The previous tail is left intact: a dropped utterance adds
                    // no text and must not sever the continuity of the real ones.
                    DeckleVadSource.Log.UtteranceDroppedNoSpeech(u.Index);
                    continue;
                }
                else
                {
                    samples = trimmed;
                }
            }
            backendChunks?.Add(samples);

            // The audio length of the utterance itself, before the pad: what the
            // phantom filter and the logs judge, so a 0.6 s blip stays a 0.6 s
            // blip and the pad cannot buy it a longer duration budget.
            int speechSamples = samples.Length;

            // Whisper sometimes ended the text early when the audio stopped on
            // near-digital silence after the trim; a noise tail fixed it on
            // replay, digital silence did not (see TrailingNoisePad). The pad
            // goes only to the backend: BackendAudio keeps the unpadded corpus.
            float[] backendSamples = TrailingNoisePad.Append(samples);

            // What the backend actually receives: after the DSP and the trim.
            // The level is measured on the utterance without the pad, which
            // would otherwise pull the RMS down; pad_ms reports it apart.
            if (OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Transcription,
                    DeckleWhispSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Pipeline))
            {
                var (rmsDbfs, peakDbfs) = SampleLevels.Measure(samples);
                DeckleWhispSource.Log.UtteranceSentToBackend(
                    u.Index, speechSamples / 16, rmsDbfs, peakDbfs, TrailingNoisePad.PadMs);
            }

            TranscriptionResult result;
            try
            {
                result = await _backend.TranscribeAsync(
                    backendSamples,
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
                // Flagged whatever the policy: a live take delivers the other
                // utterances anyway, but when none of them gave text the caller
                // must tell a failed take from a silent one.
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

            // A short blip decoded into confident-looking words. The backend has
            // already decoded it, so its native context holds the phantom text:
            // the tail is dropped like after an empty utterance, so the next call
            // starts clean of it. (Segments already streamed to NewSegment stay
            // shown live; the delivered text is what this removes.)
            if (text.Length > 0)
            {
                PhantomUtteranceFilter.Verdict verdict = PhantomUtteranceFilter.Evaluate(
                    result.Segments, speechSamples / 16_000.0);
                if (verdict.Rejected)
                {
                    DeckleWhispSource.Log.UtterancePhantomRejected(
                        u.Index, verdict.Reason, (int)(speechSamples * 1000L / 16_000),
                        verdict.TextTokens, verdict.TokensPerSecond,
                        verdict.AverageProbability, verdict.MinimumProbability);
                    DeckleWhispSource.Log.ConsumerUtterance(
                        u.Index, result.TotalDurationMs, 0,
                        result.Segments.Count, backlogAfter, result.Aborted);
                    previousTail = null;
                    continue;
                }
            }

            if (text.Length > 0)
            {
                if (sb.Length > 0) sb.Append(pendingParagraphBreak ? "\n\n" : " ");
                sb.Append(text);
                pendingParagraphBreak = u.EndedOnSilence;
                UtteranceTranscribed?.Invoke(new TranscribedUtterance(u.Index, text, u.EndedOnSilence));
                // After the utterance feeds: with a batch run, the delivered
                // text is the rewriter's view (batches already rewritten, this
                // utterance in its open batch), not the raw assembly.
                RaiseTranscriptChanged(
                    policy.DeliveredText?.Invoke() ?? sb.ToString(),
                    TranscriptChangeKind.Appended);
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
    // audio, so the caller falls back to the raw take. Incomplete: the prime
    // failed, or an utterance was skipped on a backend failure; a policy that
    // rejects incomplete results also flags an aborted or failed-code result.
    private readonly record struct StreamingConsumeResult(
        string Text, long TotalMs, long InitMs, int NSegments, int NUtterances,
        float[]? BackendAudio, bool Incomplete);

    // StopCaptureOnPrimeFailure: the live dictation, whose capture keeps running
    // while the consumer waits at the prime gate, ends it when the prime fails.
    // A file batch has no capture to stop and handles the failed gate itself.
    //
    // DeliveredText, when set, is where the take's delivered text differs from
    // the consumer's raw assembly: a live take with a batch run reads it from
    // the rewriter, so the Appended snapshots carry the rewrites already back.
    // The function returns null while the take has no run yet (a take waiting
    // for its first length step): the raw assembly then stands.
    private readonly record struct SegmentedTranscriptionPolicy(
        string FixedPrompt,
        bool ApplyPreprocessing,
        bool RejectIncompleteResults,
        bool StopCaptureOnPrimeFailure = false,
        Func<string?>? DeliveredText = null);

    internal static bool IsFileTranscriptionResultUsable(TranscriptionResult result) =>
        !result.Aborted && result.ResultCode == 0;
}

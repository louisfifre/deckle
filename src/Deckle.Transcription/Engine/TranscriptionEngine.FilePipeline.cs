using System.Diagnostics.Tracing;
using System.Threading.Channels;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    private const int FileFrameSamples = 800; // 50 ms at 16 kHz, same as live capture.

    // One worker owns one picker selection from start to finish. A bounded
    // decode channel is the first producer/consumer boundary: while the sole
    // ASR consumer transcribes file N, the producer prepares N+1, but never more
    // than one full PCM buffer waits in memory. Each prepared file then feeds
    // the exact segmented session used by live capture.
    private void FileWorkerRun(FileTranscriptionBatch batch)
    {
        using var activity = TranscriptionActivityScope.Open();
        CancellationTokenSource recordCts = _recordCts
            ?? throw new InvalidOperationException("File producer cancellation was not published before worker start.");
        CancellationTokenSource drainCts = _drainCts
            ?? throw new InvalidOperationException("File consumer cancellation was not published before worker start.");

        Task<bool> primeTask = Task.FromResult(true);
        Task decodeProducer = Task.CompletedTask;
        TranscriptionOutcome batchOutcome = TranscriptionOutcome.None;

        try
        {
            DeckleWhispSource.Log.FileTranscriptionBatchStarted();
            DeckleWhispSource.Log.FileTranscriptionBatchStartedDetail(
                batch.AudioFilePaths.Count,
                PreparedFileProducer.Capacity);
            primeTask = BeginPrime(drainCts.Token);

            Channel<PreparedFileTranscription> preparedFiles =
                PreparedFileProducer.CreateChannel();

            decodeProducer = Task.Run(
                () => PreparedFileProducer.ProduceAsync(
                    batch.AudioFilePaths,
                    preparedFiles.Writer,
                    AudioFileDecoder.Decode,
                    HandleDecodeFailure,
                    HandleDecodeException,
                    recordCts.Token),
                recordCts.Token);

            bool transcribingStarted = false;
            ConsumePreparedFilesAsync(
                preparedFiles.Reader,
                primeTask,
                () => transcribingStarted,
                () => transcribingStarted = true,
                outcome => batchOutcome = CombineFileOutcomes(batchOutcome, outcome),
                drainCts.Token).GetAwaiter().GetResult();

            decodeProducer.GetAwaiter().GetResult();
            DeckleWhispSource.Log.FileTranscriptionBatchCompleted();
            DeckleWhispSource.Log.FileTranscriptionBatchCompletedDetail(
                batch.AudioFilePaths.Count,
                batchOutcome.ToString());
            RaiseFinished(batchOutcome);
        }
        catch (OperationCanceledException)
        {
            DeckleCancellationSource.Log.OperationCancelled(
                "file-transcription-batch", "upstream", -1);
            RaiseFinished(TranscriptionOutcome.None);
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.PipelineCrashed();
            DeckleWhispSource.Log.PipelineCrashedDetail(ex.GetType().Name, ex.Message);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_PipelineCrashed_Title"),
                Loc.Get("Engine_PipelineCrashed_Body"),
                FB_REPLACEMENT);
            RaiseFinished(TranscriptionOutcome.None);
        }
        finally
        {
            // Observe the producer before disposing the token it reads. A failed
            // or cancelled producer may already have been observed above; the
            // second await is intentionally swallowed during teardown.
            try { decodeProducer.GetAwaiter().GetResult(); } catch { /* reported above */ }
            SettleWorkerToIdle(primeTask, recordCts, drainCts);
        }
    }

    private static void HandleDecodeFailure(AudioFileDecodeResult decoded)
    {
        var (title, body) = LocalizeDecodeFailure(decoded.Status);
        DeckleWhispSource.Log.FileDecodeFailed();
        DeckleWhispSource.Log.FileDecodeFailedDetail(decoded.Status.ToString());
        EmitUserFeedback(FB_ERROR, title, body, FB_OVERLAY);
    }

    private static void HandleDecodeException(string path, Exception exception)
    {
        var (title, body) = LocalizeDecodeFailure(AudioFileDecodeStatus.ReadError);
        DeckleWhispSource.Log.FileDecodeFailed();
        DeckleWhispSource.Log.FileDecodeFailedDetail(
            $"{AudioFileDecodeStatus.ReadError}:{exception.GetType().Name}");
        EmitUserFeedback(FB_ERROR, title, body, FB_OVERLAY);
    }

    private async Task ConsumePreparedFilesAsync(
        ChannelReader<PreparedFileTranscription> reader,
        Task<bool> primeTask,
        Func<bool> hasStarted,
        Action markStarted,
        Action<TranscriptionOutcome> recordOutcome,
        CancellationToken cancellationToken)
    {
        await foreach (PreparedFileTranscription prepared in
            reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // An empty decode contributes no pipeline input. Do not claim the
            // batch's Starting → Transcribing edge yet: a later valid file still
            // needs to publish the one Transcribing status that moves the HUD out
            // of Charging.
            if (prepared.Audio.Length == 0)
            {
                DeckleWhispSource.Log.TranscribeEmpty();
                continue;
            }

            bool first = !hasStarted();
            if (first)
            {
                if (Interlocked.CompareExchange(
                        ref _state,
                        (int)PipelineState.Transcribing,
                        (int)PipelineState.Starting)
                    != (int)PipelineState.Starting)
                {
                    DeckleWhispSource.Log.TranscribeSkipped(
                        ((PipelineState)Volatile.Read(ref _state)).ToString());
                    return;
                }

                markStarted();
            }

            _transcriptionId = Guid.NewGuid().ToString("N");
            DeckleWhispSource.Log.TranscriptionCorrelation(_transcriptionId);
            DeckleWhispSource.Log.FileTranscriptionStarted();
            if (OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Transcription,
                    DeckleWhispSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Pipeline))
            {
                DeckleWhispSource.Log.FileTranscriptionStartedDetail(
                    prepared.SourcePath,
                    SafeFileLength(prepared.SourcePath));
            }

            try
            {
                PipelineProduction? production = await TranscribePreparedFileAsync(
                    prepared,
                    primeTask,
                    first,
                    cancellationToken).ConfigureAwait(false);
                if (production is null)
                    continue;

                TranscriptionOutcome outcome = FinalizeTranscription(
                    production.Value,
                    TranscriptionDelivery.AdjacentFile(prepared.SourcePath),
                    announceCompletion: false);
                recordOutcome(outcome);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One bad item must not discard the rest of an immutable batch.
                // The batch worker remains alive and the next prepared file uses
                // a fresh segmented session.
                DeckleWhispSource.Log.TranscribeFailed();
                DeckleWhispSource.Log.TranscribeFailedDetail(-1);
                DeckleWhispSource.Log.PipelineCrashedDetail(
                    ex.GetType().Name,
                    ex.Message);
                EmitUserFeedback(FB_ERROR,
                    Loc.Get("Engine_TranscriptionFailed_Title"),
                    Loc.Get("Engine_TranscriptionFailed_Body"),
                    FB_OVERLAY);
            }
        }
    }

    private async Task<PipelineProduction?> TranscribePreparedFileAsync(
        PreparedFileTranscription prepared,
        Task<bool> primeTask,
        bool announceTranscribing,
        CancellationToken cancellationToken)
    {
        if (!await AwaitPrime(primeTask, "during_file_decode").ConfigureAwait(false)
            || !_backend.IsModelLoaded)
        {
            RaiseStatus(Loc.Get("Status_ModelNotReady"));
            return null;
        }

        if (announceTranscribing)
            RaiseStatus(Loc.Get("Status_Transcribing"));

        var policy = new SegmentedTranscriptionPolicy(
            FixedPrompt: string.Empty,
            ApplyPreprocessing: false,
            RejectIncompleteResults: true);
        SegmentedTranscriptionSession<StreamingConsumeResult> session =
            StartSegmentedTranscription(policy, cancellationToken, primeTask);

        Exception? pushError = null;
        try
        {
            PushDecodedAudio(session, prepared.Audio);
        }
        catch (Exception ex)
        {
            pushError = ex;
        }
        finally
        {
            session.Complete();
        }

        StreamingConsumeResult consumed =
            await session.Completion.ConfigureAwait(false);
        if (pushError is not null)
            throw pushError;
        if (consumed.Incomplete)
        {
            DeckleWhispSource.Log.TranscribeFailed();
            DeckleWhispSource.Log.TranscribeFailedDetail(-1);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_TranscriptionFailed_Title"),
                Loc.Get("Engine_TranscriptionFailed_Body"),
                FB_OVERLAY);
            return null;
        }

        string fullText = consumed.Text;
        DeckleWhispSource.Log.TranscribeCompleted();
        DeckleWhispSource.Log.TranscribeCompleteDetail(
            consumed.TotalMs,
            consumed.NSegments,
            fullText.Length);

        if (string.IsNullOrWhiteSpace(fullText))
            return null;

        ReadOnlyMemory<float> backendAudio =
            consumed.BackendAudio ?? prepared.Audio;
        return new PipelineProduction(
            RawText:           fullText,
            RawAudio:          prepared.Audio,
            BackendAudio:      backendAudio,
            TotalTranscribeMs: consumed.TotalMs,
            InitMs:            consumed.InitMs,
            NSegments:         consumed.NSegments);
    }

    private static void PushDecodedAudio(
        SegmentedTranscriptionSession<StreamingConsumeResult> session,
        ReadOnlyMemory<float> audio)
    {
        ReadOnlySpan<float> samples = audio.Span;
        for (int offset = 0; offset < samples.Length; offset += FileFrameSamples)
        {
            int count = Math.Min(FileFrameSamples, samples.Length - offset);
            var frame = new float[FileFrameSamples];
            samples.Slice(offset, count).CopyTo(frame);

            double sumSquares = 0;
            for (int i = 0; i < count; i++)
                sumSquares += frame[i] * frame[i];
            float rms = count == 0 ? 0 : (float)Math.Sqrt(sumSquares / count);

            session.Push(new CaptureFrame(frame, rms));
        }
    }

    private static TranscriptionOutcome CombineFileOutcomes(
        TranscriptionOutcome current,
        TranscriptionOutcome next)
    {
        if (current == TranscriptionOutcome.SavedToFile
            || next == TranscriptionOutcome.SavedToFile)
            return TranscriptionOutcome.SavedToFile;

        if (current == TranscriptionOutcome.ClipboardOnly
            || next == TranscriptionOutcome.ClipboardOnly)
            return TranscriptionOutcome.ClipboardOnly;

        return TranscriptionOutcome.None;
    }

    private static (string Title, string Body) LocalizeDecodeFailure(
        AudioFileDecodeStatus status)
    {
        string body = status switch
        {
            AudioFileDecodeStatus.FileNotFound      => Loc.Get("FileTranscription_DecodeFailed_Body_FileNotFound"),
            AudioFileDecodeStatus.UnsupportedFormat => Loc.Get("FileTranscription_DecodeFailed_Body_UnsupportedFormat"),
            AudioFileDecodeStatus.NoAudioTrack      => Loc.Get("FileTranscription_DecodeFailed_Body_NoAudioTrack"),
            AudioFileDecodeStatus.ProtectedContent  => Loc.Get("FileTranscription_DecodeFailed_Body_ProtectedContent"),
            _                                       => Loc.Get("FileTranscription_DecodeFailed_Body_ReadError"),
        };
        return (Loc.Get("FileTranscription_DecodeFailed_Title"), body);
    }

    private static long SafeFileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

}

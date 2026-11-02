using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;

namespace Deckle.Transcription;

/// <summary>How <see cref="TranscriptionEngine.RecoverTakeFromAudioAsync"/> ended.</summary>
public enum RecoveredTakeStatus
{
    /// <summary>The text is on the clipboard.</summary>
    Delivered,

    /// <summary>The engine was busy or disposed: nothing ran.</summary>
    NotStarted,

    /// <summary>The audio ran through and held no speech.</summary>
    NoText,

    /// <summary>The run stopped on a failure (model, transcription, clipboard, crash).</summary>
    Failed,
}

/// <param name="Status">How the run ended.</param>
/// <param name="Chars">Length of the text put on the clipboard; 0 unless <c>Delivered</c>.</param>
public readonly record struct RecoveredTake(RecoveredTakeStatus Status, int Chars = 0);

public sealed partial class TranscriptionEngine
{
    // ── AudioRecovery partial — transcribe an interrupted take from its saved audio ───
    //
    // The other half of TranscriptionEngine.TakeAudio.cs: at launch the app
    // reads the audio a lost take left on disk and hands it here. The run is an
    // engine take of its own kind, started the way a file batch is (Idle →
    // Starting, one worker), so the state machine guards it: a hotkey press
    // meanwhile is refused as busy, the model is primed as for any take, and
    // Dispose aborts it.
    //
    // The audio goes through the same segmented pipeline as a live dictation
    // (StartSegmentedTranscription, fed like a file by PushDecodedAudio), with
    // the dictation's prompt and preprocessing. Unlike a live take it rejects an
    // incomplete result: a skipped utterance would deliver a text with holes and
    // let the caller delete the only copy of the audio, so the run fails and the
    // caller keeps the audio for another try.
    //
    // Delivery is the raw text on the clipboard, never a paste and never a
    // rewrite: the person is not at the keyboard of a target, and this mirrors
    // the text recovery at launch (App.Startup.DictationRecovery.cs). The caller
    // tells the person; the engine raises Finished with None, so the HUD hides
    // its "transcribing" state without announcing a copy that the caller
    // announces better.
    //
    // This run never writes the take audio file (it is the file's reader) nor
    // the text recovery file; the caller owns both and the order of deleting
    // them.

    /// <summary>
    /// Transcribes <paramref name="audio"/> (mono 16 kHz) as one engine take and
    /// puts the text on the clipboard. Never throws for a failed run; a refused
    /// start comes back as <see cref="RecoveredTakeStatus.NotStarted"/>.
    /// </summary>
    public Task<RecoveredTake> RecoverTakeFromAudioAsync(float[] audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var notStarted = Task.FromResult(new RecoveredTake(RecoveredTakeStatus.NotStarted));
        if (_disposed) return notStarted;

        var done = new TaskCompletionSource<RecoveredTake>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ToggleResult started;
        lock (_lifecycleLock)
            started = TryStartAudioRecoveryLocked(audio, done);

        return started == ToggleResult.Started ? done.Task : notStarted;
    }

    // Same start protocol as a file batch: CAS Idle → Starting, then the worker
    // owns the state until it settles back to Idle.
    private ToggleResult TryStartAudioRecoveryLocked(
        float[] audio, TaskCompletionSource<RecoveredTake> done)
    {
        if (_disposed) return ToggleResult.IgnoredDisposed;

        if (Interlocked.CompareExchange(
                ref _state,
                (int)PipelineState.Starting,
                (int)PipelineState.Idle)
            != (int)PipelineState.Idle)
        {
            return (PipelineState)Volatile.Read(ref _state) == PipelineState.Disposed
                ? ToggleResult.IgnoredDisposed
                : ToggleResult.IgnoredBusy;
        }

        _idleEvent.Reset();

        bool committed = false;
        try
        {
            // No hotkey, capture or stop phase: leave the stage timers at zero so
            // a previous dictation's stopwatches never read as this run's.
            _modelLoadMs         = 0;
            _primeMs             = 0;
            _hotkeySw            = null;
            _recordingSw         = null;
            _recordDrainDuration = System.TimeSpan.Zero;
            _stopToPipelineSw    = null;
            _shouldPaste         = false;

            _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            InitializeRunCancellation();
            _worker = new Thread(() => AudioRecoveryWorkerRun(audio, done))
            {
                IsBackground = true,
                Name = "TranscriptionEngine.RecoveryWorker",
            };
            _worker.Start();

            committed = true;
            return ToggleResult.Started;
        }
        finally
        {
            if (!committed)
            {
                DiscardUnstartedRunCancellation();
                RollbackToIdle();
            }
        }
    }

    private void AudioRecoveryWorkerRun(float[] audio, TaskCompletionSource<RecoveredTake> done)
    {
        using var activity = TranscriptionActivityScope.Open();
        CancellationTokenSource recordCts = _recordCts
            ?? throw new InvalidOperationException("Recovery cancellation was not published before worker start.");
        CancellationTokenSource drainCts = _drainCts
            ?? throw new InvalidOperationException("Recovery drain cancellation was not published before worker start.");

        Task<bool> primeTask = Task.FromResult(true);
        var result = new RecoveredTake(RecoveredTakeStatus.Failed);
        DeckleWhispSource.Log.WorkerStarted("recovery");

        try
        {
            // The prime loads the model while nothing else waits on it, and is
            // crossed before the first backend call, as for any take.
            primeTask = BeginPrime(drainCts.Token);

            _transcriptionId = Guid.NewGuid().ToString("N");
            DeckleWhispSource.Log.TranscriptionCorrelation(_transcriptionId);
            BeginTake(TakeKinds.AudioRecovery);

            result = TranscribeRecoveredAudioAsync(audio, primeTask, drainCts.Token)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Dispose aborted the run.
            EndTake(TakeOutcomes.Abandoned, TakeEndReasons.Cancelled);
        }
        catch (Exception ex)
        {
            // No feedback: the caller falls back to the text copy and the person
            // is not waiting on this run; the log carries the cause.
            DeckleWhispSource.Log.PipelineCrashed(ex.GetType().Name, ex.Message);
            EndTake(TakeOutcomes.Failed, TakeEndReasons.PipelineCrashed);
        }
        finally
        {
            try
            {
                RaiseFinished(TranscriptionOutcome.None);
                SettleWorkerToIdle("recovery", primeTask, recordCts, drainCts);
            }
            finally
            {
                // After the engine is back to Idle, so the caller can start a take.
                done.TrySetResult(result);
            }
        }
    }

    private async Task<RecoveredTake> TranscribeRecoveredAudioAsync(
        float[] audio, Task<bool> primeTask, CancellationToken cancellationToken)
    {
        var failed = new RecoveredTake(RecoveredTakeStatus.Failed);

        // Starting → Transcribing; losing this CAS means Dispose won.
        if (Interlocked.CompareExchange(
                ref _state,
                (int)PipelineState.Transcribing,
                (int)PipelineState.Starting)
            != (int)PipelineState.Starting)
        {
            DeckleWhispSource.Log.TranscribeSkipped(((PipelineState)Volatile.Read(ref _state)).ToString());
            EndTake(TakeOutcomes.None, TakeEndReasons.StateLost);
            return failed;
        }

        // Shown while the model loads: the person sees the app is working.
        RaiseStatus(Loc.Get("Status_Transcribing"));

        if (audio.Length == 0)
        {
            EndTake(TakeOutcomes.None, TakeEndReasons.EmptyAudio);
            return new RecoveredTake(RecoveredTakeStatus.NoText);
        }

        if (!await AwaitPrime(primeTask, "during_recovery").ConfigureAwait(false)
            || !_backend.IsModelLoaded)
        {
            // The load cause sits on ModelUnavailable, and LoadModel has told the person.
            EndTake(TakeOutcomes.Failed, TakeEndReasons.ModelNotReady);
            return failed;
        }

        var policy = new SegmentedTranscriptionPolicy(
            FixedPrompt: _host.Transcription.Engine.InitialPrompt ?? "",
            ApplyPreprocessing: true,
            RejectIncompleteResults: true);
        SegmentedTranscriptionSession<StreamingConsumeResult> session =
            StartSegmentedTranscription(policy, cancellationToken, primeTask);

        Exception? pushError = null;
        try
        {
            PushDecodedAudio(session, audio);
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
            // The per-utterance cause is on UtteranceSkipped or ConsumerUtterance.
            EndTake(TakeOutcomes.Failed, TakeEndReasons.Incomplete);
            return failed;
        }

        string text = consumed.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            EndTake(TakeOutcomes.None, TakeEndReasons.NoText);
            return new RecoveredTake(RecoveredTakeStatus.NoText);
        }

        // CopyToClipboard logs its own failure and tells the person why.
        if (!CopyToClipboard(text))
        {
            EndTake(TakeOutcomes.Failed, TakeEndReasons.CopyFailed);
            return failed;
        }

        EndTake(TakeOutcomes.ClipboardOnly, TakeEndReasons.Delivered);
        return new RecoveredTake(RecoveredTakeStatus.Delivered, text.Length);
    }
}

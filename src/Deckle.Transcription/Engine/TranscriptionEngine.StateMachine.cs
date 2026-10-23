using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using System.Diagnostics.Tracing;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── StateMachine partial — RequestToggle + WorkerRun + state transitions ────────────────────────────────────────

    // How long capture keeps running after the Stop press. The press lands
    // while the user is still finishing the last word, and the waveIn loop
    // would otherwise stop within one 50 ms buffer, cutting that word. The tail
    // catches its end plus a little real trailing silence, which Whisper also
    // ends on better than on a clipped word. The HUD has already switched to
    // "transcribing" on the press, so the tail is hidden behind that transition.
    // 400 ms: long enough to cover the end of a word and a breath, short enough
    // that the added wait stays under the threshold where a stop feels laggy.
    // Not a setting: nobody should tune it per take.
    private const int CAPTURE_TAIL_AFTER_STOP_MS = 400;

    // ── Hotkey toggle entry point ───────────────────────────────────────────
    //
    // All hotkey Start/Stop traffic flows through here. The engine
    // CAS-transitions the state and returns the outcome; the caller switches on
    // it only to drive HUD and logs, never to decide what to do. Reading state
    // in the caller and then branching is the double-press race: a second
    // press lands between the read and the call.
    //
    // The rewrite applied at finalize is the persisted active profile
    // (RewriteProfileSelection.ForDictation); a press carries no profile.
    public ToggleResult RequestToggle(bool shouldPaste)
    {
        // _disposed is set before Dispose moves the state to Disposed, so it
        // covers the moment between "Quit clicked" and "Dispose finished".
        if (_disposed) return ToggleResult.IgnoredDisposed;

        var current = (PipelineState)Volatile.Read(ref _state);
        if (current == PipelineState.Disposed) return ToggleResult.IgnoredDisposed;

        if (current == PipelineState.Recording)
        {
            // Try to claim the Recording → Stopping transition. If we lose
            // the CAS, another thread already moved the state out of
            // Recording (cap duration hitting at the same instant, a race
            // with another Stop press). Treat as busy; no second action.
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)PipelineState.Stopping,
                    (int)PipelineState.Recording)
                != (int)PipelineState.Recording)
            {
                return ToggleResult.IgnoredBusy;
            }

            // Stopped by the strategy at the backend handoff (monolithic) or
            // after the consumer drain (streaming). The CAS above makes this
            // run at most once per recording. It starts at the press, so the
            // capture tail below is part of the stop-to-pipeline time the log
            // reports.
            _stopToPipelineSw = System.Diagnostics.Stopwatch.StartNew();

            // Signal the capture loop to drain and return, after a short tail
            // (see CAPTURE_TAIL_AFTER_STOP_MS). Only this user Stop press gets
            // the tail: Dispose cancels _recordCts at once so shutdown is never
            // delayed, and the file pipeline's token is cancelled by Dispose
            // only. A press landing inside the tail finds the state already
            // Stopping and is refused as IgnoredBusy above. A cap hit inside
            // the tail is harmless: its Recording → Stopping CAS simply fails.
            // CancelAfter is safe even if Record() has already returned or the
            // worker has disposed the source (ObjectDisposedException caught;
            // disposing the source also disposes its pending timer).
            try { _recordCts?.CancelAfter(CAPTURE_TAIL_AFTER_STOP_MS); }
            catch (ObjectDisposedException) { /* worker raced our cancel */ }
            return ToggleResult.Stopped;
        }

        if (current != PipelineState.Idle)
        {
            // The previous pipeline is still in flight: ignore the press, as
            // Windows 11 voice typing does. The Verbose line answers "I pressed
            // but nothing happened".
            DeckleWhispSource.Log.HotkeyToggleIgnored(current.ToString());
            return ToggleResult.IgnoredBusy;
        }

        return TryStartFromIdle(shouldPaste);
    }

    // ── File-transcription entry point ──────────────────────────────────────
    //
    // Enqueues one tray selection as an immutable batch. The engine starts one
    // batch whenever it is Idle, then keeps the same lifecycle until every file
    // in that selection has crossed the shared segmented consumer.
    // A live dictation temporarily holds the consumer without rejecting or
    // dropping queued files.
    public int EnqueueFileTranscriptions(IEnumerable<string> audioFilePaths)
    {
        if (_disposed) return 0;

        int added = _fileTranscriptionQueue.Enqueue(audioFilePaths);
        if (added > 0)
            TryConsumeNextFileTranscription();

        return added;
    }

    private void TryConsumeNextFileTranscription()
    {
        if (_disposed || IsBusy)
            return;

        _fileTranscriptionQueue.TryStartNext(
            TryStartFileTranscription);
    }

    // Starts one queued selection. Busy means "leave the whole batch at the FIFO
    // head", never reject it or let a later selection interleave it.
    private ToggleResult TryStartFileTranscription(FileTranscriptionBatch batch)
    {
        lock (_lifecycleLock)
            return TryStartFileTranscriptionLocked(batch);
    }

    private ToggleResult TryStartFileTranscriptionLocked(FileTranscriptionBatch batch)
    {
        if (_disposed) return ToggleResult.IgnoredDisposed;

        // CAS Idle → Starting up front, exactly as TryStartFromIdle does for the
        // hotkey. A file request that lands while a pipeline is in flight rebounds
        // cleanly instead of double-spawning a worker. There is no Recording
        // branch: a file run is never a Stop, so any non-Idle state is a refusal.
        if (Interlocked.CompareExchange(
                ref _state,
                (int)PipelineState.Starting,
                (int)PipelineState.Idle)
            != (int)PipelineState.Idle)
        {
            var current = (PipelineState)Volatile.Read(ref _state);
            if (current == PipelineState.Disposed) return ToggleResult.IgnoredDisposed;

            return ToggleResult.IgnoredBusy;
        }

        // We own the Idle → Starting edge. _idleEvent stays reset until either the
        // rollback below or the file worker's terminal Idle transition.
        _idleEvent.Reset();

        bool committed = false;
        try
        {
            // Per-run latency timers read by the shared finalize. A file run has
            // no hotkey→capture, drain, or stop-to-pipeline phase — leave those at
            // their zero/null defaults (finalize null-coalesces them to 0) so a
            // prior dictation run's stopwatches never leak into this run's log.
            // Only _modelLoadMs / _primeMs get filled, by the prime.
            _modelLoadMs         = 0;
            _primeMs             = 0;
            _hotkeySw            = null;
            _recordingSw         = null;
            _recordDrainDuration = System.TimeSpan.Zero;
            _stopToPipelineSw    = null;

            // A file batch never pastes and never rewrites. Delivery is carried
            // explicitly by each prepared item rather than hidden in engine-wide
            // mutable state.
            _shouldPaste = false;

            // Cancel any pending idle unload — a pipeline is starting.
            _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // Queue Charging before the worker can publish Transcribing. This
            // fires once for the complete selection, so the chrono spans the batch
            // instead of restarting between its files.
            FileTranscriptionStarted?.Invoke();

            // A subscriber may synchronously dispose the engine. Do not publish
            // or start work after that re-entrant shutdown.
            if (_disposed
                || Volatile.Read(ref _state) != (int)PipelineState.Starting)
                return ToggleResult.IgnoredDisposed;

            InitializeRunCancellation();
            _worker = new Thread(() => FileWorkerRun(batch))
            {
                IsBackground = true,
                Name = "TranscriptionEngine.FileWorker",
                Priority = ThreadPriority.AboveNormal,
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

    // Idle → Starting → Recording. CAS Idle → Starting up front so a second
    // press arriving during the mic probe rebounds immediately: the whole
    // Starting window excludes any other start, even while
    // MicrophoneCapture.Probe() is blocked on the Win32 audio device.
    //
    // CRITICAL: every early-return path below MUST roll the state back to
    // Idle, otherwise the engine permanently locks out future hotkeys. The
    // try/finally with `committed` ensures this.
    private ToggleResult TryStartFromIdle(bool shouldPaste)
    {
        lock (_lifecycleLock)
            return TryStartFromIdleLocked(shouldPaste);
    }

    private ToggleResult TryStartFromIdleLocked(bool shouldPaste)
    {
        if (Interlocked.CompareExchange(
                ref _state,
                (int)PipelineState.Starting,
                (int)PipelineState.Idle)
            != (int)PipelineState.Idle)
        {
            // Lost the CAS — another thread (Dispose, parallel hotkey)
            // moved the state out of Idle in the small window since
            // RequestToggle's snapshot read.
            return ToggleResult.IgnoredBusy;
        }

        // From here we own the Idle → Starting → (Recording or Idle) edge.
        // _idleEvent is reset until either RollbackToIdle below, or the
        // worker's terminal Idle transition.
        _idleEvent.Reset();

        bool committed = false;
        try
        {
            _modelLoadMs = 0;
            _primeMs = 0;
            _hotkeySw = System.Diagnostics.Stopwatch.StartNew();
            _recordDrainDuration = System.TimeSpan.Zero;
            _stopToPipelineSw = null;

            // Probe the audio device BEFORE "Recording" can fire. If the mic is
            // absent or busy, short-circuit the entire pipeline: no HUD chrono,
            // no worker thread, no empty transcription.
            var probe = _capture.Probe(_host.Audio.AudioInputDeviceId);
            if (!probe.Ok)
            {
                var (title, body) = LocalizeMicError(probe.Kind, probe.MmsysErr);
                OpenMicrophoneIncident("probe", probe.MmsysErr);
                EmitUserFeedback(FB_ERROR, title, body, FB_REPLACEMENT);
                return ToggleResult.IgnoredBusy;
            }

            _shouldPaste = shouldPaste;

            // Cancel any pending idle unload — a new pipeline is starting.
            _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // CAS Starting → Recording. From this moment the worker thread
            // owns the state machine — Stop, cap-duration, and the worker's
            // terminal finally are the only legitimate writers of _state.
            // The only thread that can compete here is Dispose (which moves
            // any state to Disposed); guard explicitly.
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)PipelineState.Recording,
                    (int)PipelineState.Starting)
                != (int)PipelineState.Starting)
            {
                DeckleWhispSource.Log.HotkeyStartingCASLost();
                return ToggleResult.IgnoredDisposed;
            }

            // AboveNormal, not Normal: this thread runs the waveIn capture loop,
            // which only needs to be scheduled promptly when the buffer-done event
            // fires (every 50 ms) and sleeps the rest of the time. At Normal it
            // loses its slot to the concurrent Whisper inference on the thread
            // pool; once a wait overruns the 200 ms ring (4 × 50 ms buffers) the
            // driver has no free buffer and drops incoming audio. The thread is
            // near-idle, so this starves nothing. Highest is avoided: it would
            // contend with the UI thread.
            InitializeRunCancellation();
            _worker = new Thread(WorkerRun)
            {
                IsBackground = true,
                Name = "TranscriptionEngine.Worker",
                Priority = ThreadPriority.AboveNormal,
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

    private void InitializeRunCancellation()
    {
        _recordCts = new CancellationTokenSource();
        _drainCts = new CancellationTokenSource();
    }

    private void DiscardUnstartedRunCancellation()
    {
        _recordCts?.Dispose();
        _recordCts = null;
        _drainCts?.Dispose();
        _drainCts = null;
    }

    // Rolls an uncommitted start back to Idle. The worker's terminal Idle
    // transition (SettleWorkerToIdle) must NOT call this helper: it also emits
    // "Ready" and must skip when Disposed has won.
    private void RollbackToIdle()
    {
        // Only roll back from Starting — never overwrite Recording (worker
        // already running) or Disposed. The worker spawn site is the
        // commit point; if we reach here without committing, _state is
        // still Starting unless Dispose has intervened.
        if (Interlocked.CompareExchange(
                ref _state,
                (int)PipelineState.Idle,
                (int)PipelineState.Starting)
            != (int)PipelineState.Starting)
        {
            // Dispose won: leave _state alone.
            _idleEvent.Set();
            return;
        }
        _idleEvent.Set();
    }

    // Dictation worker body: runs the selected strategy, then finalize. Its
    // teardown (SettleWorkerToIdle) is the only site that emits "Ready" on the
    // success path.
    private void WorkerRun()
    {
        using var activity = TranscriptionActivityScope.Open();
        // Paired with WorkerStopped in SettleWorkerToIdle, which every exit reaches.
        DeckleWhispSource.Log.WorkerStarted("dictation");
        CancellationTokenSource recordCts = _recordCts
            ?? throw new InvalidOperationException("Recording cancellation was not published before worker start.");
        CancellationTokenSource drainCts = _drainCts
            ?? throw new InvalidOperationException("Drain cancellation was not published before worker start.");

        // Observed in the finally even on early-exit paths that bypass the gate
        // (e.g. a mic error before the first backend call), so default it to a
        // completed true for the warm / never-started case.
        Task<bool> primeTask = Task.FromResult(true);

        try
        {
            // The prime runs CONCURRENTLY with the capture the strategy starts;
            // the strategy crosses its gate (AwaitPrime) before the first
            // backend call. See BeginPrime.
            primeTask = BeginPrime(drainCts.Token);

            _transcriptionId = System.Guid.NewGuid().ToString("N");
            DeckleWhispSource.Log.TranscriptionCorrelation(_transcriptionId);
            BeginTake(TakeKinds.Dictation);

            // The selected strategy owns capture, the backend call(s), and every
            // state transition through to Transcribing. It returns null when it
            // already handled an early exit and raised Finished itself.
            //
            // Read from the live settings, so the next recording picks up a
            // Settings change with no restart. Streaming also gets the drain
            // token for its consumer.
            bool streaming = _host.Transcription.Streaming.Strategy == PipelineStrategyKind.Streaming;
            PipelineProduction? produced =
                (streaming
                    ? ProduceStreamingAsync(recordCts.Token, drainCts.Token, primeTask)
                    : ProduceMonolithicAsync(recordCts.Token, primeTask))
                .GetAwaiter().GetResult();

            // The recovery file is cleared inside finalize, once the raw copy
            // has landed (TranscriptionEngine.Recovery.cs). A take that ends
            // here without that copy keeps it.
            if (produced is not null)
                FinalizeTranscription(produced.Value, TranscriptionDelivery.Dictation);
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.PipelineCrashed(ex.GetType().Name, ex.Message);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_PipelineCrashed_Title"),
                Loc.Get("Engine_PipelineCrashed_Body"),
                FB_REPLACEMENT);
            FinishTake(TranscriptionOutcome.None, TakeEndReasons.PipelineCrashed);
        }
        finally
        {
            SettleWorkerToIdle("dictation", primeTask, recordCts, drainCts);
        }
    }

    // Terminal teardown shared by the mic worker and the file worker. The
    // sequence is lockout-critical, so it lives once rather than in each
    // worker's finally: settle the prime, CAS to Idle, arm the idle unload
    // when the model is still resident, dispose the run's cancellation tokens,
    // then emit "Ready", in that order.
    private void SettleWorkerToIdle(
        string worker,
        Task<bool> primeTask,
        CancellationTokenSource recordCts,
        CancellationTokenSource drainCts)
    {
        // Settle the prime before any teardown. This covers the early exits
        // that bypass the gate (a mic error before the first backend call) and
        // makes the IsModelLoaded check below reflect the load result. It MUST
        // run before the drain token is disposed: the prime's abort_callback
        // polls it, and touching a disposed CTS can throw across the native
        // boundary. On Dispose the token is already cancelled, so the dummy
        // inference aborts; a model load in flight is not cancellable and runs
        // to completion. A load failure already surfaced its UserFeedback.
        try { primeTask.GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            // The prime failure already surfaced (WarmupFailed, UserFeedback);
            // this records that the teardown observed it.
            DeckleWhispSource.Log.TeardownExceptionIgnored("prime_settle", ex.GetType().Name, ex.Message);
        }

        // The lifecycle lock keeps a new start out until the old worker
        // reference and its exact cancellation channels are cleared. Status
        // fires after that, so a subscriber sees Idle when "Ready" arrives.
        //
        // ★ THIS IS THE ONLY SITE THAT EMITS "Ready" ON THE SUCCESS PATH.
        // UnloadModel also emits it after an idle unload, gated on
        // _state == Idle so the two never race.
        //
        // CAS loop instead of Exchange so a concurrent Dispose wins cleanly:
        // every attempt re-reads _state, sees Disposed, and bails out.
        // Disposed must persist past the worker's exit; a "Ready" emitted
        // after Dispose would re-arm the tray on a half-shut-down engine.
        // The take's end is declared on every normal path; one that was not
        // (a path that returns without raising Finished) is closed here, so the
        // journal never holds a start without its end.
        EndTake(TakeOutcomes.Abandoned, TakeEndReasons.WorkerExit);

        bool reachedIdle;
        lock (_lifecycleLock)
        {
            int prev;
            while (true)
            {
                prev = Volatile.Read(ref _state);
                if (prev == (int)PipelineState.Disposed) break;
                if (Interlocked.CompareExchange(
                        ref _state, (int)PipelineState.Idle, prev) == prev)
                {
                    break;
                }
            }
            reachedIdle = prev != (int)PipelineState.Disposed;
            _worker = null;

            // Dispose exactly the channels owned by this worker before another
            // Idle start can publish replacements into the engine fields.
            recordCts.Dispose();
            if (ReferenceEquals(_recordCts, recordCts))
                _recordCts = null;

            drainCts.Dispose();
            if (ReferenceEquals(_drainCts, drainCts))
                _drainCts = null;

            // Arm the idle-unload whenever the worker exits with the model
            // resident and the engine is still live. A new start is excluded by
            // the lifecycle lock until both the timer and old run are settled.
            if (reachedIdle && _backend.IsModelLoaded)
                ResetIdleTimer();

            _idleEvent.Set();
            DeckleWhispSource.Log.WorkerStopped(worker, reachedIdle);
            if (reachedIdle)
                RaiseStatus(Loc.Get("Status_Ready"));
        }
        if (reachedIdle)
        {
            TryConsumeNextFileTranscription();
        }
    }

    // Fired by MicrophoneCapture the instant waveInStart confirms the mic is
    // live, on the worker thread inside Record. Everything glued to the actual
    // start of capture happens here, so the HUD chrono does not lead the audio
    // by the device-open latency. On a cold worker "Recording" fires while the
    // model is still loading: the first backend call, not the chrono, waits
    // for the warm model (AwaitPrime).
    private void OnCaptureStarted()
    {
        CloseMicrophoneIncident();
        _hotkeySw?.Stop();
        _recordingSw = System.Diagnostics.Stopwatch.StartNew();
        RaiseStatus(Loc.Get("Status_Recording"));
    }

}

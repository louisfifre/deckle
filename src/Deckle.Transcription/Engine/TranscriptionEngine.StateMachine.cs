using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── StateMachine partial — RequestToggle + WorkerRun + state transitions ────────────────────────────────────────

    // ── Hotkey toggle entry point ───────────────────────────────────────────
    //
    // All hotkey-driven Start/Stop traffic flows through here. The earlier
    // pattern read engine state from App and branched into StartRecording or
    // StopRecording — that read-then-branch was the original double-press
    // race (App.OnHotkey reading IsRecording, then calling Start, while a
    // second press arrived between the read and the call). The current
    // contract: the engine atomically CAS-transitions the state machine and
    // returns the result; the caller only switches on the outcome to drive
    // HUD and logs, never to decide what to do.
    //
    // requireProfile: passed by App for rewrite hotkeys. When a press lands
    // in Idle but the rewrite slot has no profile bound, refuse with
    // IgnoredNoProfile so the press doesn't start an empty rewrite session.
    // Pressed during Recording, requireProfile is silent — the press is a
    // valid Stop irrespective of profile binding.
    public ToggleResult RequestToggle(string? manualProfileName, bool shouldPaste, bool requireProfile)
    {
        // _disposed is set before Dispose CAS's the state to Disposed, so it
        // catches the moment between "Quit clicked" and "Dispose finished".
        // Either guard reaching this branch means the engine is shutting
        // down — silent no-op.
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

            // Stop-to-pipeline latency starts here — closed by the
            // whisper.cpp log hook on the first whisper_vad line. The CAS
            // above guarantees this runs at most once per recording cycle,
            // so we don't need the previous "if (_isRecording)" guard.
            _stopToPipelineSw = System.Diagnostics.Stopwatch.StartNew();

            // Signal the capture polling loop to drain and return. The CTS
            // is created at the top of WorkerRun and disposed at its
            // finally; Cancel() is safe even if Record() has already
            // returned (the token is then unobserved).
            try { _recordCts?.Cancel(); }
            catch (ObjectDisposedException) { /* worker raced our cancel */ }
            return ToggleResult.Stopped;
        }

        if (current != PipelineState.Idle)
        {
            // Starting / Stopping / Transcribing — the previous pipeline is
            // still in flight. Silent no-op, only a Verbose telemetry line
            // for diagnosis when the user reports "I pressed but nothing
            // happened". Decision: ignore (Settings Win11 voice-typing
            // semantics).
            DeckleWhispSource.Log.HotkeyToggleIgnored(current.ToString());
            return ToggleResult.IgnoredBusy;
        }

        if (requireProfile && string.IsNullOrWhiteSpace(manualProfileName))
        {
            // Rewrite hotkey from Idle without a profile bound — same
            // semantics as before the refactor: warn and refuse. The press
            // does NOT take the Idle → Starting CAS, so a subsequent
            // transcribe-hotkey press will still start cleanly.
            return ToggleResult.IgnoredNoProfile;
        }

        return TryStartFromIdle(manualProfileName, shouldPaste);
    }

    // ── File-transcription entry point ──────────────────────────────────────
    //
    // A tray-driven request to transcribe one audio file. Shares the whole
    // downstream machinery with dictation — the same CAS guard, the same prime,
    // the same backend, the same HUD states, the same shared finalize — but
    // enters WITHOUT a microphone: no probe, no capture, no chrono. The decode
    // (Media Foundation → 16 kHz mono float) and the single backend call both run
    // on the file worker; the shared finalize takes the file tail (no rewrite, no
    // paste, no corpus — write the transcript to disk + clipboard).
    //
    // Returns Started when the file worker was spawned, IgnoredBusy when a
    // dictation or another file run holds the engine (the busy feedback is emitted
    // HERE — the contract has the App do nothing on this result), IgnoredDisposed
    // during shutdown. Never Stopped: a file run is a one-shot, never a toggle.
    public ToggleResult RequestFileTranscription(string audioFilePath)
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

            // Engine busy. Unlike the dictation path (silent, telemetry-only —
            // the user is mid-recording and knows it), the file entry is a direct
            // tray action with no HUD showing, so the refusal must be visible: the
            // engine owns the busy feedback here (Overlay, non-blocking).
            DeckleWhispSource.Log.FileTranscriptionIgnored(current.ToString());
            EmitUserFeedback(FB_WARN,
                Loc.Get("FileTranscription_Busy_Title"),
                Loc.Get("FileTranscription_Busy_Body"),
                FB_OVERLAY);
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

            // A file run never pastes and never rewrites (V1). Force both off so
            // the shared finalize takes the file-save tail whatever the dictation
            // defaults are; stash the path the file tail and the decode both read.
            _shouldPaste           = false;
            _manualProfileName     = null;
            _fileTranscriptionPath = audioFilePath;

            // Cancel any pending idle unload — a pipeline is starting.
            _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // Spawn the file worker while the state is still Starting: it owns the
            // Starting → Transcribing → Idle edge from here (no Recording — there
            // is no capture and no chrono, so that phase is skipped entirely). Same
            // background/priority flags as the mic worker; the distinct name marks
            // it in the thread list and the logs.
            _worker = new Thread(FileWorkerRun)
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
                RollbackToIdle();
            }
        }
    }

    // Idle → Starting → Recording. Called only when RequestToggle has
    // verified the engine is in Idle and (for rewrite hotkeys) the profile
    // is bound. CAS Idle → Starting up front so a second hotkey press
    // arriving inside the warmup gate or the mic probe rebounds immediately.
    // The entire Starting window is mutually exclusive with any other Start
    // attempt, even one that fires while MicrophoneCapture.Probe() is
    // blocked on the Win32 audio device (~1-2 ms typical, but can spike on
    // contended hardware).
    //
    // CRITICAL: every early-return path below MUST roll the state back to
    // Idle and signal _idleEvent, otherwise the engine permanently locks
    // out future hotkeys. The try/finally with `committed` ensures this.
    private ToggleResult TryStartFromIdle(string? manualProfileName, bool shouldPaste)
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
            // Reset per-run latency stage timers. _modelLoadMs is overwritten
            // by LoadModel() if it runs (cold path); _hotkeySw is stopped
            // after waveInStart via the MicrophoneCapture.CaptureStarted
            // event; _recordDrainDuration is set from CaptureResult on
            // Record() return; _stopToPipelineSw is stopped by the producing
            // strategy at the backend handoff. The backend phase timings
            // (init / VAD) now travel on PipelineProduction, not engine fields.
            _modelLoadMs = 0;
            _primeMs = 0;
            _hotkeySw = System.Diagnostics.Stopwatch.StartNew();
            _recordDrainDuration = System.TimeSpan.Zero;
            _stopToPipelineSw = null;

            // Probe the audio device BEFORE firing StatusChanged("Recording").
            // If the mic is absent/busy, short-circuit the entire pipeline:
            // no HUD chrono, no worker thread, no Transcribe(empty).
            var probe = _capture.Probe(_host.Audio.AudioInputDeviceId);
            if (!probe.Ok)
            {
                var (title, body) = LocalizeMicError(probe.Kind, probe.MmsysErr);
                DeckleWhispSource.Log.RecordingProbeFailed();
                DeckleWhispSource.Log.RecordingProbeFailedDetail(probe.MmsysErr, title);
                EmitUserFeedback(FB_ERROR, title, body, FB_REPLACEMENT);
                return ToggleResult.IgnoredBusy;
            }

            _shouldPaste           = shouldPaste;
            _manualProfileName     = manualProfileName;
            // Clear any file path left by a prior file-transcription run so this
            // dictation run takes the mic tail in FinalizeTranscription, not the
            // file tail.
            _fileTranscriptionPath = null;

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

            // Spawn the worker thread. WorkerRun owns the Recording →
            // Stopping → Transcribing → Idle transitions (and the Stopping
            // → Transcribing CAS in particular — the cap-duration branch
            // inside Record() does the Recording → Stopping CAS).
            // AboveNormal, not Normal: this thread runs the waveIn capture loop,
            // which only needs to be scheduled promptly when the buffer-done event
            // fires (every 50 ms) — it sleeps the rest of the time. At Normal it
            // loses its slot to the concurrent Whisper inference on the threadpool,
            // and telemetry showed the WaitForSingleObject(100) returning at
            // 200-480 ms instead of ~50 ms: past the 200 ms ring (4×50 ms) the
            // driver has no free buffer and drops incoming audio. AboveNormal lets
            // the producer win its slot; the thread is near-idle so this starves
            // nothing. Highest is avoided — it would contend with the UI thread.
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
                RollbackToIdle();
            }
        }
    }

    // Resets the state machine to Idle from Starting (hotkey-thread early-
    // return path). Worker-owned terminal Idle transitions live in
    // WorkerRun's finally — they must NOT call this helper because they
    // need to also emit "Ready" while skipping if Disposed has won.
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
            // Dispose won — leave _state alone, just signal idle for any
            // Dispose Wait that may be pending.
            _idleEvent.Set();
            return;
        }
        _idleEvent.Set();
    }

    // Worker thread body. Runs the full Record → Transcribe pipeline,
    // performs the worker-owned state transitions, and is the ONLY site
    // that emits "Ready" on the success path. Any RaiseStatus(Loc.Get("Status_Ready"))
    // elsewhere in this file (UnloadModel, Transcribe early-returns) must
    // also gate on _state == Idle to avoid clobbering this invariant.
    private void WorkerRun()
    {
        // Cancellation channels for this run, recreated each run so a previous
        // Cancel() doesn't leak into the next session. _recordCts stops the
        // producer (capture) — Stop and Dispose both fire it. _drainCts aborts
        // the streaming consumer's in-flight inference — Dispose only, so a Stop
        // drains the queued utterances losslessly.
        _recordCts = new CancellationTokenSource();
        _drainCts  = new CancellationTokenSource();

        // Observed in the finally even on early-exit paths that bypass the gate
        // (e.g. a mic error before the first backend call), so default it to a
        // completed true for the warm / never-started case.
        Task<bool> primeTask = Task.FromResult(true);

        try
        {
            // Kick the model prime off CONCURRENTLY with the capture below. On a
            // cold worker — model not resident, i.e. the first hotkey of the
            // session or the first after an idle unload — BeginPrime loads the
            // model AND runs a dummy inference (GPU kernels compiled) on the thread
            // pool, while Record already captures real audio. The chrono ticks from
            // the first PCM, so the old "Charging" dead time is gone. A warm worker
            // gets an already-completed gate. The prime rides the DRAIN token, so a
            // Stop lets it finish warming the model for the take that follows; only
            // Dispose aborts it. The first real backend call waits on this gate
            // (AwaitPrime, inside the strategy), so the prime's dummy whisper_full
            // and the real one never overlap.
            primeTask = BeginPrime(_drainCts.Token);

            // The recording-duration stopwatch and the "Recording" status fire from
            // OnCaptureStarted — the instant waveInStart confirms the mic is live —
            // so the HUD chrono is glued to the first real PCM. Capture starts right
            // away now (the prime runs alongside it), so on a cold worker the HUD
            // leaves Charging for Recording at once instead of after the load. The
            // invariant shifted: "Recording" MAY precede a warm model now, but the
            // first BACKEND CALL may not — the gate holds it until the prime is done.

            // One id per recording under the corpus join contract, shared by
            // whichever strategy runs and consumed only in FinalizeTranscription.
            _transcriptionId = System.Guid.NewGuid().ToString("N");
            DeckleWhispSource.Log.TranscriptionCorrelation(_transcriptionId);

            // The selected strategy owns capture, the backend call(s), and every
            // state transition through to Transcribing (cap-hit CAS, auto-
            // calibrate, Stopping → Transcribing). It returns the raw text +
            // audio + backend timings for the shared finalize, or null when it
            // already handled an early exit (mic error, empty audio, backend
            // failure, lost CAS, prime failure) and raised Finished itself.
            //
            // Strategy is read from the live settings snapshot, so the next
            // recording picks up a Settings change with no restart. Both strategies
            // take the prime gate; streaming also gets the drain token for its
            // consumer, monolithic only the producer token.
            bool streaming = _host.Transcription.Streaming.Strategy == PipelineStrategyKind.Streaming;
            PipelineProduction? produced =
                (streaming
                    ? ProduceStreamingAsync(_recordCts.Token, _drainCts.Token, primeTask)
                    : ProduceMonolithicAsync(_recordCts.Token, primeTask))
                .GetAwaiter().GetResult();

            if (produced is not null)
            {
                FinalizeTranscription(produced.Value);
            }
            // The idle-unload timer is (re)armed in the finally below, which
            // covers every exit that leaves the model resident — not only this
            // success path. This is what closes the old "model loaded but no
            // unload scheduled" gap (e.g. a prime followed by a mic error).
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
            SettleWorkerToIdle(primeTask);
        }
    }

    // Terminal teardown shared by the mic worker (WorkerRun) and the file worker
    // (FileWorkerRun). Both converge on the exact same lockout-critical sequence,
    // so it lives once here rather than being copied into each worker's finally —
    // a divergence between the two would be the module's documented lockout
    // hazard. Settles the prime, runs the worker-owned *→Idle CAS, arms the idle
    // unload when the model is still resident, disposes the per-run cancellation
    // tokens, then emits "Ready" — in that order.
    private void SettleWorkerToIdle(Task<bool> primeTask)
    {
        // Settle the prime before any teardown. On the normal path the gate
        // (AwaitPrime) already awaited it; this also covers the early-exit
        // paths that bypass the gate (a mic error before the first backend
        // call) and makes the _backend.IsModelLoaded check below reflect the
        // load result. It MUST run before _drainCts is disposed: the prime's
        // abort_callback polls that token, and touching a disposed CTS can
        // throw across the native boundary. On Dispose, _drainCts is already
        // cancelled, so the wait is bounded — the dummy inference aborts; a
        // model load in flight is not cancellable but completes in a few
        // seconds, well within the join timeout. The result is swallowed: a
        // prime failure already surfaced its UserFeedback inside LoadModel.
        try { primeTask.GetAwaiter().GetResult(); }
        catch { /* prime failure already surfaced; nothing user-facing here */ }

        // Terminal Idle transition — owned by the worker thread, in this
        // exact order: state, worker reference, idle event, then status.
        // The status fires last so any subscriber that reads _state from
        // a StatusChanged handler (tray tooltip, HudWindow) sees Idle by
        // the time "Ready" arrives.
        //
        // ★ THIS IS THE ONLY SITE THAT EMITS "Ready" ON THE SUCCESS PATH.
        // UnloadModel mirrors it for the cold-load case but also gates
        // on _state == Idle so the two never race.
        //
        // CAS loop instead of Exchange so a concurrent Dispose
        // transitioning *→Disposed wins cleanly: every CAS attempt re-
        // reads _state, sees Disposed, and bails out. Disposed must
        // persist past the worker's exit; a "Ready" emitted post-
        // Dispose would re-arm the tray on a half-shut-down engine.
        // The loop terminates in at most 2 iterations under contention
        // (only Dispose can compete with the worker for _state writes).
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
        bool reachedIdle = prev != (int)PipelineState.Disposed;
        _worker = null;

        // Arm the idle-unload whenever the worker exits with the model
        // resident and the engine is still live (reachedIdle). This is the
        // single arming point now: it covers the success path AND every
        // early return that primed the model then bailed (mic error after
        // prime, lost Transcribe CAS…), so a loaded model is never left in
        // VRAM with no scheduled unload. Skipped when Dispose won — the
        // timer is torn down in Dispose anyway.
        if (reachedIdle && _backend.IsModelLoaded)
        {
            ResetIdleTimer();
        }

        // Dispose and clear the per-run cancellation tokens. Done before
        // _idleEvent.Set() so a Dispose Wait that races on the event
        // doesn't see a still-live CTS field — fields read from there
        // are observably consistent with the worker exit.
        try { _recordCts?.Dispose(); }
        catch (ObjectDisposedException) { /* worker raced our dispose */ }
        _recordCts = null;

        try { _drainCts?.Dispose(); }
        catch (ObjectDisposedException) { /* worker raced our dispose */ }
        _drainCts = null;

        _idleEvent.Set();
        if (reachedIdle)
        {
            RaiseStatus(Loc.Get("Status_Ready"));
        }
    }

    // Fired by MicrophoneCapture the instant waveInStart confirms the mic is
    // live (the first real PCM is on its way). Everything that must be glued to
    // the actual start of capture happens here, not before the strategy runs, so
    // the HUD chrono no longer leads the audio by the device-open latency:
    //  - close the hotkey→capture latency stopwatch;
    //  - start the recording-duration stopwatch (read in FinalizeTranscription);
    //  - raise "Recording", which flips the HUD Charging → Recording and starts
    //    the on-screen chrono.
    // Runs on the capture/worker thread, inside Record (the same thread that
    // raised "Recording" here before). The old invariant ("Recording" cannot
    // appear before the model is warm) is deliberately gone: capture and the
    // prime now run concurrently, so on a cold worker "Recording" fires while the
    // model is still loading. What replaces it is the gate (AwaitPrime) — the
    // first backend call, not the chrono, is what waits for the warm model.
    private void OnCaptureStarted()
    {
        _hotkeySw?.Stop();
        _recordingSw = System.Diagnostics.Stopwatch.StartNew();
        RaiseStatus(Loc.Get("Status_Recording"));
    }

}

using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Audio.Telemetry;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Core.Interop;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription.Corpus;
using Deckle.Transcription.Engine;

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
                DeckleWhispSource.Log.RecordingProbeFailed(probe.MmsysErr, title);
                EmitUserFeedback(FB_ERROR, title, body, FB_REPLACEMENT);
                return ToggleResult.IgnoredBusy;
            }

            _shouldPaste       = shouldPaste;
            _manualProfileName = manualProfileName;

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
            _worker = new Thread(WorkerRun) { IsBackground = true, Name = "TranscriptionEngine.Worker" };
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
        // Cancellation channel for this run — Stop / Dispose call Cancel()
        // on it to drain the capture polling loop. Recreated each run so
        // a previous Cancel() doesn't leak into the next session.
        _recordCts = new CancellationTokenSource();

        try
        {
            // Prime before recording. On a cold worker — model not resident,
            // i.e. the first hotkey of the session or the first after an idle
            // unload — EnsurePrimed loads the model AND runs a dummy inference
            // so the GPU kernels are compiled. The HUD sits in Charging
            // (presented by App on the Started result) for the whole prime:
            // the chrono stays frozen/grey and no capture runs until the model
            // is warm. This is what guarantees the user's first real
            // transcription is never a cold miss. A warm worker (model still
            // resident from a recent transcription) falls straight through.
            // Cancellable via _recordCts — a Stop pressed during the prime
            // aborts the dummy inference and the whole start.
            if (!EnsurePrimed(_recordCts.Token))
            {
                RaiseFinished(TranscriptionOutcome.None);
                return;
            }

            // Only now does the recording actually begin: start the chrono and
            // flip the HUD Charging → Recording through the "Recording" status.
            _recordingSw = System.Diagnostics.Stopwatch.StartNew();
            RaiseStatus(Loc.Get("Status_Recording"));

            // One id per recording (corpus join key, ADR-0006), shared by
            // whichever strategy runs and consumed only in FinalizeTranscription.
            _transcriptionId = System.Guid.NewGuid().ToString("N");

            // The selected strategy owns capture, the backend call(s), and every
            // state transition through to Transcribing (cap-hit CAS, auto-
            // calibrate, Stopping → Transcribing). It returns the raw text +
            // audio + backend timings for the shared finalize, or null when it
            // already handled an early exit (mic error, empty audio, backend
            // failure, lost CAS) and raised Finished itself.
            //
            // Strategy selection is read from the live settings snapshot, so the
            // next recording picks up a Settings change with no restart. The
            // streaming branch wires in increment 3; monolithic is the only path
            // implemented today.
            PipelineProduction? produced =
                ProduceMonolithicAsync(_recordCts.Token).GetAwaiter().GetResult();

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
            DeckleWhispSource.Log.PipelineCrashed(ex.GetType().Name, ex.Message);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_PipelineCrashed_Title"),
                Loc.Get("Engine_PipelineCrashed_Body"),
                FB_REPLACEMENT);
            RaiseFinished(TranscriptionOutcome.None);
        }
        finally
        {
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

            // Dispose and clear the per-run cancellation token. Done before
            // _idleEvent.Set() so a Dispose Wait that races on the event
            // doesn't see a still-live CTS field — fields read from there
            // are observably consistent with the worker exit.
            try { _recordCts?.Dispose(); }
            catch (ObjectDisposedException) { /* worker raced our dispose */ }
            _recordCts = null;

            _idleEvent.Set();
            if (reachedIdle)
            {
                RaiseStatus(Loc.Get("Status_Ready"));
            }
        }
    }

}

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

        // Unblock a warmup in flight. The user's hotkey wins over the
        // best-effort priming work — abort_callback observes the token and
        // whisper_full bails within ~50 ms, releasing _transcribeLock so the
        // worker (spawned a few lines below) can enter Transcribe without
        // waiting out the warmup's residual decode time.
        TrySignalWarmupCancel();

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
            // Record() return; _stopToPipelineSw and _whisperInitSw are
            // stopped from the whisper.cpp log hook on the first whisper_vad
            // line.
            _modelLoadMs = 0;
            _hotkeySw = System.Diagnostics.Stopwatch.StartNew();
            _recordDrainDuration = System.TimeSpan.Zero;
            _stopToPipelineSw = null;
            _whisperInitMs = 0;
            _vadMs = 0;

            // Consume the warmup flags on the first start — surface any
            // problems detected silently at startup before the pipeline runs.
            // Interlocked.Exchange makes the consumption race-free; the CAS
            // above already prevents two concurrent Starts, so this is now
            // belt-and-braces. Kept for documentation value at the call site.
            if (System.Threading.Interlocked.Exchange(ref _warmupFlagsConsumed, 1) == 0)
            {
                if (!ModelWarmupOk)
                {
                    DeckleWhispSource.Log.WarmupFlagModelKO();
                    EmitUserFeedback(FB_ERROR,
                        Loc.Get("Engine_ModelNotReady_Title"),
                        Loc.Get("Engine_ModelNotReady_Body"),
                        FB_REPLACEMENT);
                    return ToggleResult.IgnoredBusy;
                }
                if (!OllamaWarmupOk)
                {
                    // Live re-probe avant d'émettre le warning : Ollama peut
                    // être devenu reachable entre warmup et premier hotkey
                    // (cas typique : l'utilisateur a démarré Ollama après
                    // Deckle, ou le service Windows a fini son init après
                    // les 3 essais retry du warmup). Single-shot 3s, exécuté
                    // sur thread pool pour éviter tout risque de deadlock
                    // sur le UI thread du message host.
                    bool reachableNow = false;
                    try
                    {
                        var ollama = new Llm.OllamaService(
                            () => _host.Llm.OllamaEndpoint);
                        var probeTask = Task.Run(() => ollama.IsAvailableAsync());
                        if (probeTask.Wait(TimeSpan.FromSeconds(4)))
                            reachableNow = probeTask.Result;
                    }
                    catch
                    {
                        // IsAvailableAsync is fail-soft (catch interne), mais
                        // filet sur Task.Run / Wait au cas où.
                    }

                    if (!reachableNow)
                    {
                        DeckleWhispSource.Log.WarmupFlagOllamaKO();
                        EmitUserFeedback(FB_WARN,
                            Loc.Get("Engine_RewriterUnavailable_Title"),
                            Loc.Get("Engine_RewriterUnavailable_Body"),
                            FB_OVERLAY);
                    }
                    else
                    {
                        DeckleWhispSource.Log.WarmupFlagOllamaRecovered();
                    }
                    // Proceed with recording — rewrite is optional.
                }
                if (!MicrophoneWarmupOk)
                {
                    DeckleWhispSource.Log.WarmupFlagMicKO();
                }
            }

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
            if (!EnsureModelLoaded())
            {
                RaiseFinished(TranscriptionOutcome.None);
                return;
            }

            _recordingSw = System.Diagnostics.Stopwatch.StartNew();
            RaiseStatus(Loc.Get("Status_Recording"));

            CaptureResult capture = _capture.Record(_recordingHost, _recordCts.Token);
            _recordDrainDuration = capture.DrainDuration;

            // Surface the post-capture narrative + cap-hit handling here in
            // the orchestrator (Capture only emits codes — no localized
            // text). The narratives mirror the legacy phrasing so existing
            // log readers stay calibrated.
            if (capture.Outcome == CaptureOutcome.MicError)
            {
                var (title, body) = LocalizeMicError(
                    MicErrorKind.Unavailable, capture.MmsysErr);
                DeckleWhispSource.Log.RecordingMicError(capture.MmsysErr, title);
                EmitUserFeedback(FB_ERROR, title, body, FB_REPLACEMENT);
                RaiseFinished(TranscriptionOutcome.None);
                return;
            }

            if (capture.Outcome == CaptureOutcome.CapHit)
            {
                // CAS Recording → Stopping ourselves so the rest of the
                // transition sequence below stays uniform with the user-
                // driven Stop path. If Dispose won (state already Disposed)
                // we just lose this CAS and the post-Record CAS fails too.
                //
                // _stopToPipelineSw timing semantics on cap-hit:
                // legacy code started this stopwatch at the moment the cap
                // was hit, BEFORE the drain phase. After extraction the
                // drain runs inside MicrophoneCapture.Record so we only
                // start the stopwatch on return — the metric now excludes
                // the ~100 ms drain on cap-hit (rare path; user-driven
                // Stop path is unchanged because RequestToggle starts the
                // stopwatch BEFORE Cancel()). Acceptable drift.
                if (Interlocked.CompareExchange(
                        ref _state,
                        (int)PipelineState.Stopping,
                        (int)PipelineState.Recording)
                    == (int)PipelineState.Recording)
                {
                    _stopToPipelineSw = System.Diagnostics.Stopwatch.StartNew();
                }
            }

            // Auto-calibration enveloppe — pure compute lives in
            // MicrophoneCalibrationCalculator; the ring buffer + side
            // effects (SaveSettings, ApplyLevelWindow, log) stay here.
            if (capture.Telemetry is not null)
            {
                TryAutoCalibrate(capture.Telemetry);
            }

            float[] audio = capture.Pcm;

            // Record() returns either because RequestToggle CAS'd
            // Recording → Stopping (the user pressed Stop), or because the
            // cap-duration branch CAS'd it itself. Either way the state
            // should now be Stopping; transition to Transcribing.
            // If we lose this CAS, Dispose has won — skip Transcribe.
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)PipelineState.Transcribing,
                    (int)PipelineState.Stopping)
                != (int)PipelineState.Stopping)
            {
                DeckleWhispSource.Log.TranscribeSkipped(((PipelineState)Volatile.Read(ref _state)).ToString());
                RaiseFinished(TranscriptionOutcome.None);
                return;
            }

            RaiseStatus(Loc.Get("Status_Transcribing"));
            TranscribeAsync(audio).GetAwaiter().GetResult();
            ResetIdleTimer();
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

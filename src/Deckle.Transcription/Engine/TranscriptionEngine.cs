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

// Result of a pipeline pass, consumed by the HUD post-paste handler.
//   None            — nothing to show (empty audio, empty text, error).
//   Pasted          — UIA confirmed a text field and Ctrl+V was delivered;
//                     HUD flashes "Pasted" in green.
//   ClipboardOnly   — text is on the clipboard but paste was skipped (UIA
//                     couldn't confirm, foreground was Deckle, SendInput
//                     partial…); HUD shows the Ctrl+V reminder for a few
//                     seconds. This is the safe default when in doubt.
public enum TranscriptionOutcome { None, Pasted, ClipboardOnly }

// Pipeline state — single source of truth for the recording lifecycle.
// Manipulated exclusively via Interlocked.CompareExchange on _state. Each
// transition is protected by a CAS so that rapid double-presses on the
// hotkey, or a Stop racing the cap-duration internal stop, all rebound
// cleanly instead of double-spawning a worker thread or double-calling
// whisper_full on the same context (whisper.cpp is not thread-safe across
// concurrent calls on the same context — segfault, not a managed exception).
//
// Legal transitions:
//   Idle         → Starting     (hotkey, RequestToggle entry)
//   Starting     → Recording    (mic probe ok, worker about to spawn)
//   Starting     → Idle         (probe fail / warmup gate / disposed — rollback)
//   Recording    → Stopping     (hotkey Stop OR cap duration hit)
//   Stopping     → Transcribing (worker, after Record() returns)
//   Transcribing → Idle         (worker finally, after Transcribe() exits)
//   *            → Disposed     (Dispose, terminal)
//
// See TranscriptionEngine.RequestToggle, TryStartFromIdle, and WorkerRun for the
// actual CAS sites. The cap-duration branch is now handled inside
// MicrophoneCapture / WaveInLoop — WorkerRun observes CaptureOutcome.CapHit
// on return and runs the Recording → Stopping CAS itself.
public enum PipelineState { Idle, Starting, Recording, Stopping, Transcribing, Disposed }

// Outcome of a hotkey toggle request — returned to App.OnHotkey so the
// caller can drive HUD/log without ever reading engine state directly
// (which is what caused the original double-press race).
public enum ToggleResult
{
    Started,            // CAS Idle → Starting succeeded, worker spawned.
    Stopped,            // CAS Recording → Stopping succeeded, worker draining.
    IgnoredBusy,        // State was Starting/Stopping/Transcribing — silent no-op.
    IgnoredNoProfile,   // Rewrite hotkey with no profile bound, called from Idle.
    IgnoredDisposed,    // Engine in shutdown — silent no-op.
}

// ─── Transcription engine ─────────────────────────────────────────────────────
//
// Ported from WhispInteropTest (WhispForm) into a standalone class.
// UI-framework independent — communicates via events.
// Events may fire from background threads: subscribers are responsible
// for marshaling to the UI thread.

public sealed partial class TranscriptionEngine : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────

    // Fired from the loading thread or from StartRecording/Transcribe.
    // Subscriber must marshal to UI thread via DispatcherQueue.TryEnqueue.
    public event Action<string>?  StatusChanged;

    // Fired at the very end of Transcribe(), regardless of exit path
    // (model not ready, empty text, normal exit). The outcome tells the HUD
    // whether text was actually delivered, so it can show a short "Copied"
    // confirmation on success, a "Ctrl+V" reminder when the clipboard holds
    // the result but paste was refused, or hide silently when there's
    // nothing meaningful to report (errors, empty audio, empty text).
    // Background thread → subscriber responsible for marshaling.
    public event Action<TranscriptionOutcome>? TranscriptionFinished;

    // Synchronous rendezvous just before PasteFromClipboard. The caller
    // (App.xaml.cs) hooks HudWindow.HideSync() to ensure no activation
    // mutation from Deckle occurs while SendInput is in flight to the target.
    public Action? OnReadyToPaste { get; set; }

    // Microphone level, linear RMS [0, 1], throttled ~20 Hz (one emission per
    // 50 ms sub-window of the captured audio). Fired from the recording thread
    // — subscribers marshal to UI. Consumer-less for now (HUD contour animation
    // will hook in later).
    public event Action<float>? AudioLevel;

    // Per-segment notification streamed by the active IAsrBackend during
    // inference — fired immediately after the backend's native callback
    // (or backend's equivalent) appends the segment to its internal list.
    // Subscribers marshal to UI; this fires from the backend's inference
    // thread.
    public event Action<TranscriptionSegment>? NewSegment;

    // All StatusChanged / TranscriptionFinished emissions route through these two
    // helpers. The prime (EnsurePrimed) used to silence them via a ThreadStatic
    // warmup flag; the prime now calls the backend directly and never reaches
    // these helpers (nor the clipboard, corpus, or finalize), so there is nothing
    // left to gate — they always fire.
    private void RaiseStatus(string status)
    {
        StatusChanged?.Invoke(status);
    }

    private void RaiseFinished(TranscriptionOutcome outcome)
    {
        TranscriptionFinished?.Invoke(outcome);
    }

    // ── Internal state ───────────────────────────────────────────────────────

    // ── UserFeedback emission ───────────────────────────────────────────────
    //
    // Minimal wrapper over the Whisp provider's `UserFeedbackEmitted`
    // EventSource event. Severity and Role pass as primitives, preserving the
    // semantics of the old `UserFeedback{Severity,Role}` enum (Wave 6b)
    // without pulling on Deckle.Logging. The host sink `AppHudFeedbackSink`
    // routes each event to the main surface (Replacement) or stack (Overlay)
    // according to `role`.
    private const int FB_INFO          = 0;
    private const int FB_WARN          = 1;
    private const int FB_ERROR         = 2;
    private const int FB_REPLACEMENT   = 0;
    private const int FB_OVERLAY       = 1;

    private static void EmitUserFeedback(int severity, string title, string body, int role = FB_REPLACEMENT)
        => DeckleWhispSource.Log.UserFeedbackEmitted(severity, title, body, role);

    private readonly LlmService _llm;

    // ASR backend — every call into whisper.cpp (or any future inference
    // engine) goes through this interface. The orchestrator never touches
    // P/Invoke, callbacks, or native structs directly.
    private readonly IAsrBackend _backend;

    // volatile: prevents the JIT from caching the flag in a CPU register.
    private volatile bool _shouldPaste = false;

    // Pipeline state — single source of truth, manipulated only via
    // Interlocked.CompareExchange on _state. Backed by int because Interlocked
    // doesn't operate on enums directly — cast to/from PipelineState at every
    // read/write site. See the PipelineState enum above for the legal
    // transitions and the rationale.
    //
    // Invariant: every public entry point (RequestToggle, Dispose, UnloadModel)
    // reads _state exactly once via Volatile.Read, then either CAS-transitions
    // it or rebounds with a no-op. The worker thread owns the
    // Stopping → Transcribing → Idle transitions; no other thread may write
    // those.
    private int _state = (int)PipelineState.Idle;

    // Cancellation channel for the active capture session (the PRODUCER).
    // Recreated at the top of each WorkerRun, cancelled by the Stop path
    // (RequestToggle after CAS Recording → Stopping) AND by Dispose. Disposed
    // inside WorkerRun's finally once the run has returned.
    //
    // The cap-duration branch is owned by MicrophoneCapture / WaveInLoop — the
    // orchestrator observes it via CaptureResult.Outcome on return.
    private CancellationTokenSource? _recordCts;

    // Second cancellation channel for the streaming CONSUMER (the per-utterance
    // transcription loop). Fired ONLY by Dispose, never by Stop — this is what
    // lets a user Stop drain the queued utterances losslessly (the consumer is
    // not cancelled) while Dispose aborts the in-flight inference so the worker
    // join stays bounded and whisper_free never runs on an active context. The
    // monolithic path ignores it; only the streaming pipeline observes it.
    private CancellationTokenSource? _drainCts;

    // Signaled when the engine returns to Idle (worker exits + state reset).
    // Initialised "set" because no recording is in flight at construction time.
    // Reset on Idle → Starting, set on the worker's terminal Idle transition
    // (the same finally that emits "Ready"). Used by Dispose to await the
    // running pipeline within a bounded timeout — never read from the hotkey
    // path, which relies on the CAS itself for re-entry refusal.
    private readonly ManualResetEventSlim _idleEvent = new(initialState: true);

    // Reference to the live worker thread (Record + Transcribe). Held only
    // for Dispose to call Join with a timeout — no other consumer reads this.
    // null when Idle.
    private Thread? _worker;

    // Name of the rewrite profile chosen by the hotkey that started this
    // recording (null = no manual rewrite; fall back to AutoRewriteRules
    // based on recording duration). Captured when the hotkey starts the run
    // (TryStartFromIdle) and consumed in FinalizeTranscription.
    private string?         _manualProfileName = null;

    // Stable identifier for the current pipeline invocation. Regenerated once
    // per recording in WorkerRun (before the strategy runs); stamped on every
    // corpus event emitted for this transcription and on the WAV file basename
    // so the JSONL lines and the audio file join unambiguously. 32 hex chars
    // (Guid "N" format): see ADR-0006.
    private string          _transcriptionId   = "";

    // Model lifecycle: lazy load on first hotkey, unload after idle timeout.
    // The "pipeline running, don't unload" guard reads _state directly
    // (anything other than Idle / Disposed means a pipeline is in flight).
    // The lock guards the idle-unload decision; the backend owns its own
    // model lock for the actual native call.
    private readonly object _idleUnloadLock = new();
    private System.Threading.Timer? _idleTimer;
    private const int MODEL_IDLE_TIMEOUT_MS = 5 * 60 * 1000; // 5 minutes

    // Cold-load wall time captured from the most recent backend.LoadModelAsync
    // call, surfaced in the LatencyPayload. 0 when the call hit a hot model.
    private long _modelLoadMs;

    // Stopwatch started at the beginning of each recording (used for logs).
    private System.Diagnostics.Stopwatch? _recordingSw;

    // Auto-calibration ring buffer — one MicrophoneTelemetryPayload per
    // recording. Only filled when LevelWindow.AutoCalibrationEnabled is on.
    // Once the buffer has AutoCalibrationSamples entries, the engine pushes
    // a fresh MinDbfs/MaxDbfs back into Settings + HudChrono so the HUD
    // tracks the user's hardware drift without manual re-tuning. See
    // TryAutoCalibrate below for the heuristic.
    private readonly Queue<MicrophoneTelemetryPayload> _autoCalibBuffer = new();

    // Latency instrumentation — stage timers feeding LatencyPayload. Reset at
    // the entry of StartRecording so each run reports its own values.
    //
    //   _hotkeySw             — entry of StartRecording → just after waveInStart.
    //                           On a cold run includes the model load (load
    //                           runs on the worker thread before the mic
    //                           opens), plus mic probe and worker spin-up.
    //   _recordDrainDuration  — CT cancels → end of CaptureResult drain phase.
    //                           Captured from CaptureResult.DrainDuration on
    //                           Record() return.
    //   _stopToPipelineSw     — entry of RequestToggle (Stop branch) → just
    //                           before backend.TranscribeAsync. Captures the
    //                           drain + the orchestrator overhead. Add the
    //                           backend's TranscriptionResult.InitDurationMs
    //                           to get the equivalent of the legacy
    //                           "stop to first whisper_vad" measurement.
    private System.Diagnostics.Stopwatch? _hotkeySw;
    private System.TimeSpan _recordDrainDuration;
    private System.Diagnostics.Stopwatch? _stopToPipelineSw;

    private bool _disposed;

    // ── Observable properties ──────────────────────────────────────────────────

    public bool IsReady => _backend.IsModelLoaded;

    // True whenever the pipeline is in any non-Idle, non-Disposed state.
    // Read by callers that want a coarse "is something going on?" signal —
    // the hotkey path does NOT consume this (it goes through RequestToggle
    // which CAS's the transition atomically; reading and then deciding is
    // exactly the racy pattern this passe was designed to remove).
    public bool IsBusy
    {
        get
        {
            var s = (PipelineState)Volatile.Read(ref _state);
            return s != PipelineState.Idle && s != PipelineState.Disposed;
        }
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    private readonly ITranscriptionEngineHost _host;

    // Microphone capture service — extracted from the legacy 331-line Record()
    // body. The engine owns the orchestration (state machine, narratives,
    // auto-calibration enveloppe) and consumes Capture as a black-box PCM
    // producer. Future modules (Ask-Ollama) will share the same instance
    // type without dragging Whisp's transcription dependencies along.
    private readonly MicrophoneCapture _capture;

    // Adapter that exposes ITranscriptionEngineHost as IAudioRecordingHost — keeps
    // ITranscriptionEngineHost free of an IAudioRecordingHost inheritance (Ask-Ollama
    // would not have wanted that coupling).
    private readonly RecordingHostAdapter _recordingHost;

    public TranscriptionEngine(ITranscriptionEngineHost host, IAsrBackend backend)
    {
        _host = host;
        _backend = backend;

        _llm = new LlmService();

        // MicrophoneCapture emits through DeckleAudioSource (wave 2).
        // TranscriptionEngine emits through DeckleWhispSource (wave 5). No
        // dependency on LogService remains in the engine itself.
        _capture = new MicrophoneCapture();
        _recordingHost = new RecordingHostAdapter(_host);
        // Forward the per-sub-window RMS to whoever subscribes to the engine
        // (HUD chrono today). Capture stays unaware of UI consumers.
        _capture.AudioLevel += rms => AudioLevel?.Invoke(rms);
        // Close the hotkey-to-capture latency stopwatch the moment waveInStart
        // confirms the mic is live. The legacy code did this directly inside
        // Record(); the event keeps the orchestrator in charge of its own
        // stopwatches.
        _capture.CaptureStarted += () => _hotkeySw?.Stop();
        // Surface the localized low-audio overlay when the live tracker
        // detects no sustained healthy voice in the first 5 s. Capture only
        // emits a technical log; the localized UserFeedback is built here.
        _capture.LowAudioDetected += OnCaptureLowAudioDetected;

        // Backend-specific lifecycle (log hook installation, native callback
        // registration) lives inside the IAsrBackend implementation. The
        // orchestrator just consumes the contract.
        //
        // Model loaded on-demand at first hotkey press (see EnsureModelLoaded).
        // Unloaded after MODEL_IDLE_TIMEOUT_MS of inactivity to free VRAM.
    }

    // Localized UserFeedback for the low-audio condition. Capture emits a
    // bare `LowAudioDetected` event so it stays free of any Loc.Get
    // dependency; the engine owns the localization (Engine_LowAudio_Title /
    // Body) and the UserFeedback role (Overlay — capture continues).
    private void OnCaptureLowAudioDetected()
    {
        DeckleWhispSource.Log.RecordingLowAudio();
        EmitUserFeedback(FB_WARN,
            Loc.Get("Engine_LowAudio_Title"),
            Loc.Get("Engine_LowAudio_Body"),
            FB_OVERLAY);
    }

    // Adapter that maps ITranscriptionEngineHost → IAudioRecordingHost. Lives inside
    // TranscriptionEngine so the engine project owns the coupling rather than
    // requiring ITranscriptionEngineHost to inherit from IAudioRecordingHost (which would
    // bleed Capture's contract into every host that wants to drive Whisp).
    private sealed class RecordingHostAdapter : IAudioRecordingHost
    {
        private readonly ITranscriptionEngineHost _h;
        public RecordingHostAdapter(ITranscriptionEngineHost h) { _h = h; }
        public int  AudioInputDeviceId         => _h.Audio.AudioInputDeviceId;
        public int  MaxRecordingDurationSeconds => _h.Audio.MaxRecordingDurationSeconds;
        public bool MicrophoneTelemetryEnabled  => _h.Telemetry.MicrophoneTelemetry;
    }


    // ── Dispose ───────────────────────────────────────────────────────────────

    // Tray Quit → App.QuitApp → here. The state machine flips to Disposed
    // unconditionally so any in-flight worker thread or stray hotkey lands
    // on a refusal path. Then we wait for the worker to actually exit
    // before disposing the backend — the backend's own Dispose holds the
    // model lock and waits for an in-flight inference to drain on its side
    // before freeing the native context (whisper_free on a context with
    // active inference is a native segfault that no managed handler can
    // rescue, and that invariant lives inside the backend now).
    //
    // Timeout: a long inference on a GPU backend can take 5-15 s; 30 s is
    // enough for normal cases. If it expires we log a Warning and leak the
    // worker thread — the process is exiting anyway.
    private const int DISPOSE_WORKER_JOIN_TIMEOUT_MS = 30_000;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Capture before transitioning so the Verbose line below records
        // what the engine was actually doing when Dispose arrived.
        var prevState = (PipelineState)Volatile.Read(ref _state);
        Interlocked.Exchange(ref _state, (int)PipelineState.Disposed);

        // Tell the capture loop to stop, in case the worker is still in
        // there. WorkerRun's Stopping → Transcribing CAS will lose to our
        // Disposed write and skip Transcribe entirely, but it still needs
        // to exit MicrophoneCapture.Record() cleanly to release the waveIn
        // handles. CTS may already be disposed if WorkerRun raced ahead.
        try { _recordCts?.Cancel(); }
        catch (ObjectDisposedException) { }

        // Abort the streaming consumer's in-flight inference too (the drain
        // token). Without this, a Dispose mid-streaming would block the worker
        // join on a long backlog and could let the backend free run while an
        // inference is active. Fired only here — Stop never cancels it.
        try { _drainCts?.Cancel(); }
        catch (ObjectDisposedException) { }

        var worker = _worker;
        if (worker is not null && worker.IsAlive)
        {
            DeckleWhispSource.Log.DisposeStart(prevState.ToString(), DISPOSE_WORKER_JOIN_TIMEOUT_MS);
            var swJoin = System.Diagnostics.Stopwatch.StartNew();
            bool joined = worker.Join(DISPOSE_WORKER_JOIN_TIMEOUT_MS);
            swJoin.Stop();
            if (!joined)
            {
                DeckleWhispSource.Log.DisposeWorkerJoinTimeout(swJoin.ElapsedMilliseconds);
            }
            else
            {
                DeckleWhispSource.Log.DisposeWorkerJoined(swJoin.ElapsedMilliseconds);
            }
        }

        _idleTimer?.Dispose();

        // _idleEvent is intentionally NOT disposed — if the Join timed out,
        // the leaked worker may still call _idleEvent.Set() in its finally,
        // and Dispose'ing the event would turn that into an
        // ObjectDisposedException. The process is exiting anyway.

        // Backend disposal frees the native model context. The backend
        // serialises this against any inference still in flight via its
        // internal model lock; from the orchestrator's perspective we just
        // call Dispose and let the backend handle the rest.
        _backend.Dispose();

        _capture.Dispose();
    }
}

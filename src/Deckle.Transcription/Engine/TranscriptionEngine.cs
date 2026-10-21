using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;
using Deckle.Vad;

namespace Deckle.Transcription;

// Result of a pipeline pass, consumed by the HUD.
//   None            — nothing to show (empty audio, empty text, error).
//   Pasted          — UIA confirmed a text field and Ctrl+V was delivered.
//   ClipboardOnly   — text is on the clipboard but paste was skipped (UIA
//                     couldn't confirm, foreground was Deckle, SendInput
//                     partial…). The safe default when in doubt.
//   SavedToFile     — file-transcription run: the transcript was written to a
//                     .txt beside its source. Never emitted by a dictation run.
public enum TranscriptionOutcome { None, Pasted, ClipboardOnly, SavedToFile }

// Pipeline state, manipulated only through Interlocked operations on _state.
// Every transition is a CAS so rapid double-presses, or a Stop racing the
// cap-duration stop, rebound cleanly instead of double-spawning a worker or
// double-calling whisper_full on the same context (whisper.cpp is not
// thread-safe on one context: a segfault, not a managed exception).
//
// Legal transitions:
//   Idle         → Starting     (hotkey or file batch, under _lifecycleLock)
//   Starting     → Recording    (mic probe ok, worker about to spawn)
//   Starting     → Transcribing (file worker, first decoded file)
//   Starting     → Idle         (probe fail / disposed — rollback)
//   Recording    → Stopping     (hotkey Stop, or the strategy on a cap hit)
//   Stopping     → Transcribing (strategy, after Record() returns)
//   *            → Idle         (worker teardown, SettleWorkerToIdle)
//   *            → Disposed     (Dispose, terminal)
public enum PipelineState { Idle, Starting, Recording, Stopping, Transcribing, Disposed }

// Returned to App.OnHotkey so the caller drives HUD and log without reading
// engine state itself: read-then-act is the double-press race.
public enum ToggleResult
{
    Started,            // CAS Idle → Starting succeeded, worker spawned.
    Stopped,            // CAS Recording → Stopping succeeded, worker draining.
    IgnoredBusy,        // State was Starting/Stopping/Transcribing, or the mic probe failed (reported on its own).
    IgnoredDisposed,    // Engine in shutdown — silent no-op.
}

// ─── Transcription engine ─────────────────────────────────────────────────────
//
// UI-framework independent: communicates through events, which may fire from
// background threads, so subscribers marshal to the UI thread.

public sealed partial class TranscriptionEngine : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────

    // Fires on background threads (worker, idle timer); subscribers marshal.
    public event Action<string>?  StatusChanged;

    // Fires once per run on every exit path (once per batch for a file run);
    // the outcome tells the HUD what was delivered. Background thread.
    public event Action<TranscriptionOutcome>? TranscriptionFinished;

    // Fired when the file-queue consumer starts one complete tray selection, not
    // when its producer merely enqueues it. Exactly once per batch; subscribers
    // marshal to UI.
    public event Action? FileTranscriptionStarted;

    // Synchronous rendezvous just before PasteFromClipboard. The host hooks
    // HudWindow.HideSync() so no activation change from Deckle occurs while
    // SendInput is in flight to the target.
    public Action? OnReadyToPaste { get; set; }

    // Microphone level, linear RMS [0, 1], one emission per 50 ms sub-window.
    // Fired from the recording thread; subscribers marshal to UI.
    public event Action<float>? AudioLevel;

    // Live streaming capture only; emitted on the audio producer thread.
    // Independent of optional diagnostics and of the ASR consumer's latency.
    public event Action<UtteranceProgress>? UtteranceProgressChanged;

    // Per-segment notification during inference. Fires from the backend's
    // inference thread; subscribers marshal to UI.
    public event Action<TranscriptionSegment>? NewSegment;

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
    // Severity and role travel as primitives on the Whisp provider's
    // `UserFeedbackEmitted` event. The host sink `AppHudFeedbackSink` routes
    // Replacement to the HUD's main surface and Overlay to its stack.
    private const int FB_INFO          = 0;
    private const int FB_WARN          = 1;
    private const int FB_ERROR         = 2;
    private const int FB_REPLACEMENT   = 0;
    private const int FB_OVERLAY       = 1;

    private static void EmitUserFeedback(int severity, string title, string body, int role = FB_REPLACEMENT)
        => DeckleWhispSource.Log.UserFeedbackEmitted(severity, title, body, role);

    private readonly IRewriteService _rewrite;

    // Every call into whisper.cpp goes through this interface; the engine never
    // touches the backend's P/Invoke, callbacks or native structs.
    private readonly IAsrBackend _backend;

    private volatile bool _shouldPaste = false;

    // An int because Interlocked does not operate on enums; see PipelineState
    // for the legal transitions. The worker owns every transition into
    // Transcribing and back to Idle; only Dispose competes with it, moving any
    // state to Disposed.
    private int _state = (int)PipelineState.Idle;

    // Cancellation for the capture (the PRODUCER). Published before the worker
    // starts, cancelled by Stop and by Dispose, disposed when the worker
    // settles. The cap-duration stop happens inside capture; the strategy sees
    // it as CaptureResult.Outcome.
    private CancellationTokenSource? _recordCts;

    // Cancellation for the work that must survive a Stop and abort ONLY on
    // Dispose: the segmented CONSUMER (the per-utterance transcription loop)
    // and the concurrent prime. A user Stop thus drains the queued utterances
    // losslessly and lets the prime finish warming the model even on a short
    // take; Dispose aborts both so the worker join stays bounded and
    // whisper_free never runs on an active context.
    private CancellationTokenSource? _drainCts;

    // Set while Idle, reset on Idle → Starting, set again by the rollback or
    // the worker's terminal Idle transition. Nothing in the engine waits on it;
    // re-entry refusal is the CAS itself.
    private readonly ManualResetEventSlim _idleEvent = new(initialState: true);

    // Held for Dispose to Join with a timeout. null when Idle.
    private Thread? _worker;

    // Serializes the start commit point with Dispose. State CAS operations
    // reject competing starts, but they cannot by themselves make publishing
    // the worker and its cancellation channels atomic with backend teardown.
    private readonly object _lifecycleLock = new();

    // Tray selections are immutable batches; the engine is their only consumer.
    // A whole selection leaves this FIFO as one worker lifecycle, so the engine
    // never returns to Idle between its files.
    private readonly FileTranscriptionQueue _fileTranscriptionQueue = new();

    // Regenerated per recording (WorkerRun) and per file (the file consumer);
    // stamped on every corpus event and on the corpus WAV basename so the JSONL
    // lines and the audio file join unambiguously (the corpus join contract).
    private string          _transcriptionId   = "";

    // Lazy load on a cold take, unload after the idle timeout. The lock guards
    // the idle-unload decision; the backend owns its own model lock for the
    // native call.
    private readonly object _idleUnloadLock = new();
    private System.Threading.Timer? _idleTimer;
    private const int MODEL_IDLE_TIMEOUT_MS = 5 * 60 * 1000; // 5 minutes

    // Cold-load wall time for the LatencyPayload. 0 when the run found the
    // model resident.
    private long _modelLoadMs;

    // Wall time of the concurrent prime (model load + dummy inference). Set by
    // EnsurePrimed on the prime task, read by AwaitPrime at the gate (Task
    // completion is the memory barrier). Stays 0 on a warm worker, which is how
    // AwaitPrime skips the PrimeOverlap measure.
    private long _primeMs;

    // Dependency incidents outlive one take. They close only when the real
    // dependency succeeds again: waveInStart for the microphone, and a
    // successful backend load for the model.
    private int _microphoneIncidentOpen;
    private int _modelIncidentOpen;

    private void OpenMicrophoneIncident(string phase, uint mmsysErr)
    {
        if (Interlocked.Exchange(ref _microphoneIncidentOpen, 1) != 0) return;
        DeckleWhispSource.Log.MicrophoneUnavailable();
        DeckleWhispSource.Log.MicrophoneUnavailableDetail(phase, mmsysErr);
    }

    private void CloseMicrophoneIncident()
    {
        if (Interlocked.Exchange(ref _microphoneIncidentOpen, 0) == 0) return;
        DeckleWhispSource.Log.MicrophoneRecovered();
    }

    private void OpenModelIncident(string reason)
    {
        if (Interlocked.Exchange(ref _modelIncidentOpen, 1) != 0) return;
        DeckleWhispSource.Log.ModelUnavailable();
        DeckleWhispSource.Log.ModelUnavailableDetail(reason);
    }

    private bool CloseModelIncident()
        => Interlocked.Exchange(ref _modelIncidentOpen, 0) != 0;

    private System.Diagnostics.Stopwatch? _recordingSw;

    // One MicrophoneTelemetryPayload per recording; see TryAutoCalibrate.
    private readonly Queue<MicrophoneTelemetryPayload> _autoCalibBuffer = new();

    // Latency stage timers feeding LatencyPayload, reset at each run's start.
    //
    //   _hotkeySw             — start request → just after waveInStart: the mic
    //                           probe and worker spin-up only. A cold model load
    //                           runs concurrently and is reported in
    //                           _modelLoadMs and the PrimeOverlap measure.
    //   _recordDrainDuration  — capture cancelled → end of the drain, from
    //                           CaptureResult.DrainDuration.
    //   _stopToPipelineSw     — RequestToggle's Stop branch → the backend
    //                           handoff (monolithic) or the end of the
    //                           consumer drain (streaming). It includes the
    //                           capture tail kept after the press
    //                           (CAPTURE_TAIL_AFTER_STOP_MS).
    private System.Diagnostics.Stopwatch? _hotkeySw;
    private System.TimeSpan _recordDrainDuration;
    private System.Diagnostics.Stopwatch? _stopToPipelineSw;

    private volatile bool _disposed;

    // ── Observable properties ──────────────────────────────────────────────────

    public bool IsReady => _backend.IsModelLoaded;

    // A coarse "is something going on?" signal. The hotkey path must not use
    // it: reading then deciding is the double-press race RequestToggle's CAS
    // closes.
    public bool IsBusy
    {
        get
        {
            var s = (PipelineState)Volatile.Read(ref _state);
            return s != PipelineState.Idle && s != PipelineState.Disposed;
        }
    }

    // Semantic read for UI bridges that must render the current capture state
    // without interpreting a localized StatusChanged string.
    public bool IsRecording =>
        (PipelineState)Volatile.Read(ref _state) == PipelineState.Recording;

    // ── Constructor ──────────────────────────────────────────────────────────

    private readonly ITranscriptionEngineHost _host;

    private readonly IMicrophoneCapture _capture;

    // The Win32 implementation is the production default; the interface keeps
    // live-pipeline integration tests deterministic and free of global
    // clipboard side effects.
    private readonly IClipboardWriter _clipboard;

    private readonly RecordingHostAdapter _recordingHost;

    // Crash-safety copy of the live dictation (TranscriptionEngine.Recovery.cs).
    // Null switches it off.
    private readonly DictationRecoveryStore? _recovery;

    // Silero VAD lifecycle, owned by Deckle.Vad. Resolved lazily per segmented
    // session.
    private readonly VadService _vadService;

    public TranscriptionEngine(
        ITranscriptionEngineHost host,
        IAsrBackend backend,
        IRewriteService? rewrite = null,
        DictationRecoveryStore? recovery = null)
        : this(
            host,
            backend,
            new MicrophoneCapture(),
            Win32ClipboardWriter.Instance,
            rewrite,
            recovery)
    {
    }

    internal TranscriptionEngine(
        ITranscriptionEngineHost host,
        IAsrBackend backend,
        IMicrophoneCapture capture,
        IClipboardWriter clipboard,
        IRewriteService? rewrite = null,
        DictationRecoveryStore? recovery = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(clipboard);

        _host = host;
        _backend = backend;
        _capture = capture;
        _clipboard = clipboard;
        _recovery = recovery;
        _vadService = new VadService(_host.ResolveModelsDirectory);

        _rewrite = rewrite ?? new RewriteService();

        _recordingHost = new RecordingHostAdapter(_host);
        _capture.AudioLevel += rms => AudioLevel?.Invoke(rms);
        _capture.CaptureStarted += OnCaptureStarted;
        _capture.LowAudioDetected += OnCaptureLowAudioDetected;
    }

    // Capture emits a bare `LowAudioDetected` so it stays free of any Loc.Get
    // dependency; the engine owns the localization and the role (Overlay,
    // because capture continues).
    private void OnCaptureLowAudioDetected()
    {
        DeckleWhispSource.Log.RecordingLowAudio();
        EmitUserFeedback(FB_WARN,
            Loc.Get("Engine_LowAudio_Title"),
            Loc.Get("Engine_LowAudio_Body"),
            FB_OVERLAY);
    }

    // Lives here so the engine owns the coupling, rather than making
    // ITranscriptionEngineHost inherit IAudioRecordingHost and bleed Capture's
    // contract into every host.
    private sealed class RecordingHostAdapter : IAudioRecordingHost
    {
        private readonly ITranscriptionEngineHost _h;
        public RecordingHostAdapter(ITranscriptionEngineHost h) { _h = h; }
        public int  AudioInputDeviceId         => _h.Audio.AudioInputDeviceId;
        public int  MaxRecordingDurationSeconds => _h.Audio.MaxRecordingDurationSeconds;
        public bool MicrophoneTelemetryEnabled  => _h.Telemetry.MicrophoneTelemetry;
    }


    // ── Dispose ───────────────────────────────────────────────────────────────

    // The state flips to Disposed unconditionally so any in-flight worker or
    // stray hotkey lands on a refusal path; then the worker is joined before
    // the backend is disposed.
    //
    // What keeps whisper_free off an active context is this ORDERING, NOT a
    // backend lock: whisper_free runs under the backend's _modelLock and
    // whisper_full under its _transcribeLock, two locks that never contend. The
    // join returns only after the worker's teardown has settled the run and
    // waited the prime task, so a successful join means both the real
    // inference and the prime's dummy inference have returned.
    //
    // If the join times out the worker leaks, and _backend.Dispose() MUST then
    // be skipped: freeing the context while that worker may still be inside
    // whisper_full is the native segfault the join protects against. The
    // process is exiting, so the OS reclaims both.
    private const int DISPOSE_WORKER_JOIN_TIMEOUT_MS = 30_000;

    public void Dispose()
    {
        PipelineState prevState;
        Thread? worker;
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;

            // Capture before transitioning so the Verbose line below records
            // what the engine was actually doing when Dispose arrived.
            prevState = (PipelineState)Volatile.Read(ref _state);
            Interlocked.Exchange(ref _state, (int)PipelineState.Disposed);

            // Cancellation channels and the worker are published under this
            // same lock before Thread.Start. Dispose therefore sees all three
            // or none of them; it can never free the backend ahead of a late
            // worker that escaped publication.
            try { _recordCts?.Cancel(); }
            catch (ObjectDisposedException) { }

            try { _drainCts?.Cancel(); }
            catch (ObjectDisposedException) { }

            worker = _worker;
        }
        _fileTranscriptionQueue.Complete();

        // Set when the join times out: the leaked worker may still be inside a
        // whisper_full, so freeing the native context below would race it.
        bool joinTimedOut = false;
        if (worker is not null && worker.IsAlive)
        {
            DeckleWhispSource.Log.DisposeStart(prevState.ToString(), DISPOSE_WORKER_JOIN_TIMEOUT_MS);
            var swJoin = System.Diagnostics.Stopwatch.StartNew();
            bool joined = worker.Join(DISPOSE_WORKER_JOIN_TIMEOUT_MS);
            swJoin.Stop();
            if (!joined)
            {
                joinTimedOut = true;
                DeckleWhispSource.Log.DisposeWorkerJoinTimeout();
                DeckleWhispSource.Log.DisposeWorkerJoinTimeoutDetail(swJoin.ElapsedMilliseconds);
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

        // whisper_free: safe only after a successful join (see the ordering note
        // on DISPOSE_WORKER_JOIN_TIMEOUT_MS).
        if (!joinTimedOut)
            _backend.Dispose();

        _capture.Dispose();

        _vadService.Dispose();
    }
}

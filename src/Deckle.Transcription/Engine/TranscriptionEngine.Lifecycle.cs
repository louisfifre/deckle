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
    // ── Lifecycle partial — model load/unload + warmup ────────────────────────────────────────

    // ── Model lifecycle (lazy load + idle unload) ──────────────────────────────
    //
    // The model is NOT loaded at startup. It is loaded on-demand when the user
    // presses the hotkey for the first time (or after an idle unload).
    // After each transcription, an idle timer starts. When it expires without
    // a new transcription, the backend is asked to free the model.
    //
    // All native concerns (file existence, GPU init, fallback) live inside
    // the IAsrBackend implementation; the orchestrator handles only the
    // user-facing wrapper (RaiseStatus, UserFeedback localization).

    // silentStatus suppresses the "Loading model… → Ready" status transitions
    // emitted here. Set by the parallel-load path in WorkerRun, where the load
    // runs concurrently with the capture and the "Recording" status must hold
    // for the whole recording instead of being clobbered by a load that
    // happens to finish mid-capture. The error UserFeedback dialogs stay — a
    // load failure must always surface, silent or not.
    private bool LoadModel(bool silentStatus = false)
    {
        if (!silentStatus) RaiseStatus(Loc.Get("Status_LoadingModel"));

        ModelLoadResult result;
        try
        {
            // The backend's LoadModelAsync is synchronous in practice for the
            // Whisper backend (whisper_init blocks). The await here keeps the
            // signature open to truly async backends without changing the
            // orchestrator. CancellationToken.None — the orchestrator does
            // not yet cancel model loads (the original sync method had no
            // cancellation either).
            result = _backend.LoadModelAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.ModelLoadFailed(ex.Message);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ModelLoadFailed_Title"),
                Loc.Get("Engine_ModelLoadFailed_Body"),
                FB_REPLACEMENT);
            if (!silentStatus) RaiseStatus(Loc.Get("Status_Ready"));
            return false;
        }

        if (!result.Success)
        {
            // Map the backend's stable reason string to the appropriate
            // localized user feedback. "file_not_found" → model-missing
            // dialog; anything else → generic load-failed.
            if (result.ErrorReason == "file_not_found")
            {
                EmitUserFeedback(FB_ERROR,
                    Loc.Get("Engine_WhisperModelNotFound_Title"),
                    Loc.Get("Engine_WhisperModelNotFound_Body"),
                    FB_REPLACEMENT);
            }
            else
            {
                EmitUserFeedback(FB_ERROR,
                    Loc.Get("Engine_ModelLoadFailed_Title"),
                    Loc.Get("Engine_ModelLoadFailed_Body"),
                    FB_REPLACEMENT);
            }
            if (!silentStatus) RaiseStatus(Loc.Get("Status_Ready"));
            return false;
        }

        // Captured for the next LatencyPayload. Non-zero means the run paid
        // the cold-load cost; warm runs report 0.
        _modelLoadMs = result.LoadDurationMs;

        // Mirror the symmetric "Ready" emitted on the failure paths above so
        // the tray tooltip transitions Loading model… → Ready as soon as the
        // model is in memory.
        if (!silentStatus) RaiseStatus(Loc.Get("Status_Ready"));
        return true;
    }

    private bool EnsureModelLoaded(bool silentStatus = false)
    {
        if (_backend.IsModelLoaded) return true;
        lock (_idleUnloadLock)
        {
            if (_backend.IsModelLoaded) return true;
            DeckleWhispSource.Log.ModelOnDemandLoad();
            return LoadModel(silentStatus);
        }
    }

    // Frees the model to release VRAM. Called by the idle timer. Skipped if
    // a pipeline (Record+Transcribe) is currently active — reading _state
    // directly is enough; anything other than Idle means a pipeline is in
    // flight. Dispose frees the model on its own.
    private void UnloadModel()
    {
        lock (_idleUnloadLock)
        {
            var state = (PipelineState)Volatile.Read(ref _state);
            if (state != PipelineState.Idle)
            {
                DeckleWhispSource.Log.ModelIdleUnloadSkipped(state.ToString());
                return;
            }
            if (!_backend.IsModelLoaded) return;

            _backend.UnloadModel();
            DeckleWhispSource.Log.ModelUnloaded(MODEL_IDLE_TIMEOUT_MS / 1000);
            // Re-check state right before RaiseStatus — a hotkey could have
            // landed during the native free (rare, since unload only fires
            // from the idle timer in Idle state).
            if ((PipelineState)Volatile.Read(ref _state) == PipelineState.Idle)
            {
                RaiseStatus(Loc.Get("Status_Ready"));
            }
        }
    }

    private void ResetIdleTimer()
    {
        if (_idleTimer is null)
            _idleTimer = new System.Threading.Timer(_ => UnloadModel(), null, MODEL_IDLE_TIMEOUT_MS, Timeout.Infinite);
        else
            _idleTimer.Change(MODEL_IDLE_TIMEOUT_MS, Timeout.Infinite);
        DeckleWhispSource.Log.ModelIdleTimerSet(MODEL_IDLE_TIMEOUT_MS / 1000);
    }

    // ── Warmup clip loader ──────────────────────────────────────────────────
    //
    // Reads Assets/Sounds/speech.wav (deployed next to the exe via the
    // Content directive in Deckle.csproj) and converts the PCM mono 16-bit
    // 16 kHz body into the float[-1, 1] sample buffer Whisper expects.
    // Strict format check — the file is shipped pre-converted, anything
    // unexpected returns null so Warmup falls back to a silent buffer
    // instead of crashing the boot path.
    //
    // Header layout reference (canonical 44-byte PCM WAV):
    //   00..03  "RIFF"
    //   04..07  RIFF size (file - 8)
    //   08..11  "WAVE"
    //   12..15  "fmt "
    //   16..19  fmt chunk size (16 for plain PCM)
    //   20..21  audio format (1 = PCM)
    //   22..23  num channels
    //   24..27  sample rate
    //   28..31  byte rate
    //   32..33  block align
    //   34..35  bits per sample
    //   36..39  "data"
    //   40..43  data chunk size
    //   44..    int16 little-endian samples
    private float[]? TryLoadWarmupClip()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "speech.wav");
        try
        {
            if (!File.Exists(path))
            {
                DeckleWhispSource.Log.WarmupClipMissing(path);
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44
                || bytes[0]  != 'R' || bytes[1]  != 'I' || bytes[2]  != 'F' || bytes[3]  != 'F'
                || bytes[8]  != 'W' || bytes[9]  != 'A' || bytes[10] != 'V' || bytes[11] != 'E'
                || bytes[12] != 'f' || bytes[13] != 'm' || bytes[14] != 't' || bytes[15] != ' '
                || bytes[36] != 'd' || bytes[37] != 'a' || bytes[38] != 't' || bytes[39] != 'a')
            {
                DeckleWhispSource.Log.WarmupClipHeaderInvalid(path);
                return null;
            }

            int audioFormat   = BitConverter.ToInt16(bytes, 20);
            int numChannels   = BitConverter.ToInt16(bytes, 22);
            int sampleRate    = BitConverter.ToInt32(bytes, 24);
            int bitsPerSample = BitConverter.ToInt16(bytes, 34);
            int dataSize      = BitConverter.ToInt32(bytes, 40);

            if (audioFormat != 1 || numChannels != 1 || sampleRate != 16000 || bitsPerSample != 16)
            {
                DeckleWhispSource.Log.WarmupClipSampleMismatch(audioFormat, numChannels, sampleRate, bitsPerSample);
                return null;
            }

            int sampleCount = dataSize / 2;
            float[] samples = new float[sampleCount];
            int offset = 44;
            for (int i = 0; i < sampleCount; i++)
            {
                short s = BitConverter.ToInt16(bytes, offset);
                samples[i] = s / 32768f;
                offset += 2;
            }
            return samples;
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.WarmupClipLoadFailed(ex.GetType().Name, ex.Message);
            return null;
        }
    }

    // ── Prime ─────────────────────────────────────────────────────────────────
    //
    // Ensures the backend is ready for a clean first transcription: model
    // loaded AND inference kernels compiled. Called by WorkerRun *before* the
    // recording begins (the HUD sits in Charging meanwhile), not at boot — the
    // model is loaded on demand and freed again after the idle timeout, so
    // nothing sits in VRAM while the app is idle.
    //
    // Returns true immediately when the model is already resident — a warm
    // worker skips straight to recording. On a cold worker it does two things:
    //   1) Load the model, silent on the status channel (silentStatus: true).
    //      The HUD's Charging state is the user-facing "preparing" signal;
    //      LoadModel's internal "Loading model… → Ready" transitions would
    //      otherwise clobber it. A load failure surfaces its own localized
    //      UserFeedback (inside LoadModel) and returns false.
    //   2) Run a dummy inference: push the short embedded clip
    //      (Assets/Sounds/speech.wav, PCM mono 16 kHz) straight through the
    //      backend so VAD + whisper_full + the first-time GPU kernel compile all
    //      execute once. ~200–800 ms on RX 7900 XT with Vulkan ggml. On a
    //      missing/corrupt clip we fall back to a 1.6 s silent buffer — the dummy
    //      inference still compiles the kernels.
    //
    // Robustness — never touches the clipboard, the corpus, or the status /
    // Finished events. The prime calls the backend DIRECTLY (not a pipeline
    // strategy, not the shared finalize), with an empty segment sink, so there is
    // no user-facing tail to suppress — the old ThreadStatic warmup flag is gone.
    // It runs synchronously on the worker thread before "Recording" is raised, so
    // a real transcription can never observe a half-primed state.
    //
    // Cancellable via the caller's token (the run's _recordCts): a Stop pressed
    // during the prime aborts the dummy inference (abort_callback observes the
    // token mid-decoder) and returns false so the whole start unwinds. The
    // model load itself is not cancellable; a Stop during the load is observed
    // right after it returns.
    private bool EnsurePrimed(CancellationToken ct)
    {
        if (_backend.IsModelLoaded) return true;

        DeckleWhispSource.Log.WarmupStart();

        if (!EnsureModelLoaded(silentStatus: true)) return false;
        if (ct.IsCancellationRequested) return false;

        float[] warmupBuffer = TryLoadWarmupClip() ?? new float[25_600];

        // Prime the inference path DIRECTLY through the backend — not through a
        // pipeline strategy. The sole purpose is to pay the one-time cost (VAD +
        // whisper_full + first-time GPU kernel compile); the result is discarded
        // and nothing user-facing happens — no clipboard, no corpus, no
        // status/Finished, and an empty segment sink so prime segments never leak
        // to NewSegment subscribers. Going straight to the backend keeps the
        // prime off any streaming consumer thread and avoids segmenting a clip
        // that does not need it. It runs synchronously on the worker thread
        // before "Recording" is raised, so a real transcription can never observe
        // a half-primed state (this is what the old ThreadStatic warmup flag
        // guarded against — no longer needed now that the prime bypasses the
        // pipeline entirely).
        try
        {
            _backend.TranscribeAsync(warmupBuffer, static _ => { }, ct)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Stop pressed during the prime — unwind the start quietly. Any other
            // exception is a genuine prime failure and propagates to WorkerRun's
            // catch (PipelineCrashed), same as a real transcription crash.
            return false;
        }

        if (ct.IsCancellationRequested) return false;

        DeckleWhispSource.Log.WarmupComplete();
        return true;
    }

}

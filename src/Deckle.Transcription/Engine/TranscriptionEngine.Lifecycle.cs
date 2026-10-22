using System.Runtime.InteropServices;
using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Transcription;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── Lifecycle partial — model load/unload + warmup ────────────────────────────────────────

    // ── Model lifecycle (lazy load + idle unload) ──────────────────────────────
    //
    // The model is NOT loaded at startup, so nothing sits in VRAM while the app
    // is idle. It loads on the first take (or the first after an idle unload),
    // and an idle timer frees it when no new take follows. Native concerns
    // (file existence, GPU init, fallback) live in the IAsrBackend; the engine
    // handles only the user-facing status and UserFeedback.

    // silentStatus suppresses the "Loading model… → Ready" status transitions.
    // The concurrent prime sets it: the load runs alongside the capture, and
    // "Recording" must hold for the whole take. Error UserFeedback still
    // surfaces either way.
    private bool LoadModel(bool silentStatus = false)
    {
        if (!silentStatus) RaiseStatus(Loc.Get("Status_LoadingModel"));
        string workId = WorkId.Next();
        // While the incident is open every take retries the load; only the
        // first attempt opens a journal pair.
        if (Volatile.Read(ref _modelIncidentOpen) != 0)
            DeckleWhispSource.Log.ModelLoadRetrying(workId);
        else
            DeckleWhispSource.Log.ModelLoading(workId);

        ModelLoadResult result;
        try
        {
            // The Whisper backend's load is synchronous in practice
            // (whisper_init blocks), and the engine never cancels a load.
            result = _backend.LoadModelAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            OpenModelIncident(workId, "load_exception", ex.GetType().Name, ex.Message);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_ModelLoadFailed_Title"),
                Loc.Get("Engine_ModelLoadFailed_Body"),
                FB_REPLACEMENT);
            if (!silentStatus) RaiseStatus(Loc.Get("Status_Ready"));
            return false;
        }

        if (!result.Success)
        {
            OpenModelIncident(workId, result.ErrorReason ?? "load_failed", "", "");
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

        _modelLoadMs = result.LoadDurationMs;

        string device = result.Accelerator ?? "unknown";
        if (CloseModelIncident() is { } incidentId)
            DeckleWhispSource.Log.ModelRecovered(incidentId, result.LoadDurationMs, device);
        else
            DeckleWhispSource.Log.ModelLoaded(workId, result.LoadDurationMs, device);

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

    // Called by the idle timer. Skipped when _state is anything but Idle: a
    // pipeline is in flight. Dispose frees the model on its own.
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
            // A hotkey may have started a take during the native free.
            if ((PipelineState)Volatile.Read(ref _state) == PipelineState.Idle)
            {
                RaiseStatus(Loc.Get("Status_Ready"));
            }
        }
    }

    private void ResetIdleTimer()
    {
        if (_idleTimer is null)
            // A timer callback runs on the thread pool, where an escaping
            // exception ends the process; a failed unload must only be logged.
            _idleTimer = new System.Threading.Timer(_ =>
            {
                try { UnloadModel(); }
                catch (Exception ex)
                {
                    DeckleWhispSource.Log.ModelIdleUnloadFailed(ex.GetType().Name, ex.Message);
                }
            }, null, MODEL_IDLE_TIMEOUT_MS, Timeout.Infinite);
        else
            _idleTimer.Change(MODEL_IDLE_TIMEOUT_MS, Timeout.Infinite);
        DeckleWhispSource.Log.ModelIdleTimerSet(MODEL_IDLE_TIMEOUT_MS / 1000);
    }

    // ── Warmup clip loader ──────────────────────────────────────────────────
    //
    // Reads Assets/Sounds/speech.wav (deployed by the Content item in
    // Deckle.App.csproj). The file ships pre-converted to PCM mono 16-bit
    // 16 kHz, so the check is strict and expects the canonical 44-byte header
    // (fmt fields at 20..35, "data" at 36, samples from 44). Anything unexpected
    // returns null and the prime falls back to a silent buffer.
    private float[]? TryLoadWarmupClip()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "speech.wav");
        try
        {
            if (!File.Exists(path))
            {
                DeckleWhispSource.Log.WarmupClipMissing();
                DeckleWhispSource.Log.WarmupClipMissingDetail(path);
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44
                || bytes[0]  != 'R' || bytes[1]  != 'I' || bytes[2]  != 'F' || bytes[3]  != 'F'
                || bytes[8]  != 'W' || bytes[9]  != 'A' || bytes[10] != 'V' || bytes[11] != 'E'
                || bytes[12] != 'f' || bytes[13] != 'm' || bytes[14] != 't' || bytes[15] != ' '
                || bytes[36] != 'd' || bytes[37] != 'a' || bytes[38] != 't' || bytes[39] != 'a')
            {
                DeckleWhispSource.Log.WarmupClipHeaderInvalid();
                DeckleWhispSource.Log.WarmupClipHeaderInvalidDetail(path);
                return null;
            }

            int audioFormat   = BitConverter.ToInt16(bytes, 20);
            int numChannels   = BitConverter.ToInt16(bytes, 22);
            int sampleRate    = BitConverter.ToInt32(bytes, 24);
            int bitsPerSample = BitConverter.ToInt16(bytes, 34);
            int dataSize      = BitConverter.ToInt32(bytes, 40);

            if (audioFormat != 1 || numChannels != 1 || sampleRate != 16000 || bitsPerSample != 16)
            {
                DeckleWhispSource.Log.WarmupClipSampleMismatch();
                DeckleWhispSource.Log.WarmupClipSampleMismatchDetail(audioFormat, numChannels, sampleRate, bitsPerSample);
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
    // BeginPrime starts the prime for the current run and returns the gate
    // (AwaitPrime) the pipeline crosses before its first backend call.
    //
    // On a WARM worker the model is resident: no task is spawned and the gate
    // is an already-completed true.
    //
    // On a COLD worker the prime (model load + a dummy inference to compile the
    // GPU kernels) runs on the thread pool, CONCURRENTLY with the capture, so
    // the chrono ticks from the first PCM while the model warms behind it. The
    // first real backend call waits at the gate until the prime's dummy
    // whisper_full has returned, so the two never overlap.
    //
    // primeCt is the run's DRAIN token (_drainCts), never the producer/Stop token:
    // the prime must SURVIVE a Stop so the model finishes warming even on a take
    // shorter than the load, and abort only on Dispose so the worker join stays
    // bounded.
    private Task<bool> BeginPrime(CancellationToken primeCt)
    {
        if (_backend.IsModelLoaded) return Task.FromResult(true);
        return Task.Run(() => EnsurePrimed(primeCt));
    }

    // The gate every pipeline crosses before its first backend call: no real
    // whisper_full until the prime's dummy whisper_full has returned. The
    // backend's _transcribeLock is the hard native guard against a concurrent
    // call; this gate adds the ordering and the model-loaded guarantee, so the
    // real call never hits an unloaded context. PrimeOverlap is emitted only
    // when a prime actually ran.
    // gatePhase (closed vocabulary "at_stop" | "during_recording" |
    // "during_file_decode") records WHERE the wait sat, so gate_wait_ms reads as
    // one magnitude per phase. See PrimeOverlap's remark.
    private async Task<bool> AwaitPrime(Task<bool> primeTask, string gatePhase)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool primed = await primeTask.ConfigureAwait(false);
        sw.Stop();
        if (_primeMs > 0)
            DeckleWhispSource.Log.PrimeOverlap(_primeMs, sw.ElapsedMilliseconds, gatePhase);
        return primed;
    }

    // Ensures a clean first transcription: model loaded AND inference kernels
    // compiled. Runs on the thread pool via BeginPrime on a cold worker,
    // concurrently with the capture, never at boot.
    //   1) Load the model silently: the HUD already shows Recording. A load
    //      failure surfaces its own UserFeedback inside LoadModel and returns
    //      false, so the gate reports failure and the take is dropped.
    //   2) Run a dummy inference on the embedded clip so VAD, whisper_full and
    //      the first-time GPU kernel compile all execute once. A missing or
    //      corrupt clip falls back to 1.6 s of silence, which still compiles
    //      the kernels.
    //
    // The prime calls the backend DIRECTLY, with an empty segment sink: never
    // the clipboard, the corpus, the status or Finished events, nor
    // NewSegment. That bypass, not the thread it runs on, is what keeps a real
    // transcription from observing a half-primed user-facing state.
    //
    // A Stop does NOT abort the prime; only Dispose fires the drain token,
    // aborting the dummy inference (abort_callback observes it mid-decoder).
    // The model load itself is not cancellable; a Dispose during the load is
    // observed right after it returns.
    private bool EnsurePrimed(CancellationToken ct)
    {
        if (_backend.IsModelLoaded) return true;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string warmupId = WorkId.Next();
        DeckleWhispSource.Log.WarmupStart(warmupId);

        // Every exit after WarmupStart closes it: completed, cancelled or failed.
        try
        {
            return RunPrime(ct, sw, warmupId);
        }
        catch (OperationCanceledException)
        {
            DeckleWhispSource.Log.WarmupCancelled(warmupId, sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.WarmupFailed(warmupId, "prime_exception", ex.GetType().Name, ex.Message, sw.ElapsedMilliseconds);
            throw;
        }
    }

    private bool RunPrime(CancellationToken ct, System.Diagnostics.Stopwatch sw, string warmupId)
    {
        if (!EnsureModelLoaded(silentStatus: true))
        {
            // LoadModel already carries the cause on ModelUnavailable.
            DeckleWhispSource.Log.WarmupFailed(warmupId, "model_load_failed", "", "", sw.ElapsedMilliseconds);
            return false;
        }
        if (ct.IsCancellationRequested)
        {
            DeckleWhispSource.Log.WarmupCancelled(warmupId, sw.ElapsedMilliseconds);
            return false;
        }

        float[] warmupBuffer = TryLoadWarmupClip() ?? new float[25_600];

        // The discarded result is not enough to keep the prime out of the take.
        // whisper.cpp keeps the tokens a call decoded in the decoder state of
        // the context and primes the next call with them unless no_context is
        // set (checked in v1.9.1, the shipped bundle; see WhisperParamsMapper).
        // On the silent fallback Whisper decodes a subtitle credit, and on
        // 2026-10-21 that credit came back in the first real take. Setting
        // no_context on the prime would not help: it clears the context before
        // the prime decodes, not after. The isolation sits on the other side,
        // where it covers the end of a previous take too: the first call of
        // every take leaves ContinuesPreviousCall false, which sets no_context.
        try
        {
            _backend.TranscribeAsync(
                    warmupBuffer,
                    static _ => { },
                    ct,
                    new TranscriptionContext(PrimingText: null, EmitPreamble: false))
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Dispose fired the drain token during the prime: abandon quietly.
            // Any other exception is a genuine prime failure and propagates
            // through the gate to the pipeline that awaits it.
            DeckleWhispSource.Log.WarmupCancelled(warmupId, sw.ElapsedMilliseconds);
            return false;
        }

        if (ct.IsCancellationRequested)
        {
            DeckleWhispSource.Log.WarmupCancelled(warmupId, sw.ElapsedMilliseconds);
            return false;
        }

        _primeMs = sw.ElapsedMilliseconds;
        DeckleWhispSource.Log.WarmupComplete(warmupId, _primeMs);
        return true;
    }

}

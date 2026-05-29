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

    private bool LoadModel()
    {
        RaiseStatus(Loc.Get("Status_LoadingModel"));

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
            RaiseStatus(Loc.Get("Status_Ready"));
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
            RaiseStatus(Loc.Get("Status_Ready"));
            return false;
        }

        // Captured for the next LatencyPayload. Non-zero means the run paid
        // the cold-load cost; warm runs report 0.
        _modelLoadMs = result.LoadDurationMs;

        // Mirror the symmetric "Ready" emitted on the failure paths above so
        // the tray tooltip transitions Loading model… → Ready as soon as the
        // model is in memory.
        RaiseStatus(Loc.Get("Status_Ready"));
        return true;
    }

    private bool EnsureModelLoaded()
    {
        if (_backend.IsModelLoaded) return true;
        lock (_idleUnloadLock)
        {
            if (_backend.IsModelLoaded) return true;
            DeckleWhispSource.Log.ModelOnDemandLoad();
            return LoadModel();
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

    // ── Warmup ──────────────────────────────────────────────────────────────
    //
    // Runs a real "first inference" at startup so the user's first hotkey
    // press doesn't pay the cold cost (context alloc + GPU warm + Vulkan
    // pipeline compile + weight paging). We push a short embedded reference
    // clip (Assets/Sounds/speech.wav, PCM mono 16 kHz ~2 s) through the full
    // Transcribe() path so VAD finds speech, whisper_full actually decodes,
    // and the GPU pipelines are compiled once and for all. Roughly 200–800 ms
    // on RX 7900 XT with Vulkan ggml — paid here instead of on the user's
    // first dictation.
    //
    // StatusChanged / TranscriptionFinished / Narrative are gated during
    // Transcribe() via t_isWarmup (RaiseStatus / RaiseFinished /
    // RaiseNarrative) so the HUD never appears, the tray doesn't flash, and
    // LogWindow doesn't surface "Looking for speech…" / "Speech detected —
    // 2.4 s…" / "Whisper transcribed…" phrases that would confuse the user
    // at boot. Two warmup-specific narratives are emitted directly — one at
    // the start ("Priming the recognizer…") and one at the end ("Pipeline
    // ready"). LoadModel's narrative stays audible because it runs before
    // t_isWarmup flips.
    //
    // Cancellable. RequestToggle and Dispose call Cancel() on _warmupCts to
    // unblock the user — a hotkey pressed during warmup must not wait for
    // the warmup's whisper_full to finish before the recording can start.
    // The abort_callback observes the token mid-decoder, so cancellation
    // surfaces in ~50 ms rather than the worst-case ~800 ms.
    //
    // Fire-and-forget on a background thread — the call site in
    // App.OnLaunched must not block UI-thread startup. Named Warmup (not
    // WarmupAsync) because the method returns void: the *Async suffix in
    // C# is reserved for methods returning Task / ValueTask.
    public void Warmup()
    {
        if (_disposed) return;
        var thread = new Thread(() =>
        {
            // Re-check : Dispose peut survenir entre le Thread.Start et
            // le démarrage effectif du thread (rare mais possible si
            // l'utilisateur quitte très tôt après le boot).
            if (_disposed) return;

            // CTS lifetime is bounded by this thread. We assign to the field
            // so RequestToggle / Dispose can signal cancellation, and clear
            // it in finally so post-warmup callers see "no warmup in flight".
            var cts = new CancellationTokenSource();
            _warmupCts = cts;
            var ct = cts.Token;

            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                DeckleWhispSource.Log.WarmupStart();

                // 1) Mic probe — same code path StartRecording uses, just the
                //    probe result is stored instead of blocking the recording.
                _micWarmupOk = _capture.Probe(_host.Audio.AudioInputDeviceId).Ok ? 1 : 0;

                if (ct.IsCancellationRequested)
                {
                    DeckleWhispSource.Log.WarmupCancelledBeforeModel(sw.ElapsedMilliseconds);
                    return;
                }

                // 2) Model load. On failure we stop here — nothing else can
                //    be tested without the model — and flag model+ollama as
                //    failing so the first hotkey surfaces the right message.
                if (!EnsureModelLoaded())
                {
                    _modelWarmupOk  = 0;
                    _ollamaWarmupOk = 0;
                    DeckleWhispSource.Log.WarmupAbortedModelLoad(sw.ElapsedMilliseconds, MicrophoneWarmupOk);
                    return;
                }
                _modelWarmupOk = 1;

                if (ct.IsCancellationRequested)
                {
                    DeckleWhispSource.Log.WarmupCancelledBeforeTranscribe(sw.ElapsedMilliseconds);
                    return;
                }

                // Real-audio Transcribe through the full pipeline (VAD +
                // whisper_full + Vulkan kernel compile) to pay the first-
                // inference cost before any user hotkey. The clip is shipped
                // alongside the exe under Assets/Sounds/speech.wav (PCM mono
                // 16-bit 16 kHz). On load failure we fall back to a 1.6 s
                // silent buffer — the user-visible narratives are gated
                // either way, so the fallback is invisible beyond the warmup
                // log line. Length-mismatch scenarios (corrupted file, wrong
                // format) are rare but should not block startup.
                float[] warmupBuffer = TryLoadWarmupClip()
                    ?? new float[25_600];

                t_isWarmup = true;
                try
                {
                    TranscribeAsync(warmupBuffer, ct).GetAwaiter().GetResult();
                }
                finally
                {
                    t_isWarmup = false;
                }

                if (ct.IsCancellationRequested)
                {
                    DeckleWhispSource.Log.WarmupCancelledDuringTranscribe(sw.ElapsedMilliseconds);
                    return;
                }

                // 3) Ollama health-check. Skipped (and left as OK) when the LLM
                //    feature is disabled — no rewriter needed, no warning to
                //    surface. 3 s timeout par tentative dans IsAvailableAsync
                //    × 3 essais espacés de 500 ms — couvre la race classique
                //    au boot PC où Deckle démarre avant qu'Ollama ait fini
                //    d'écouter sur 11434. Pire cas borné à ~10 s.
                var llmSettings = _host.Llm;
                if (llmSettings.Enabled)
                {
                    try
                    {
                        var ollama = new Llm.OllamaService(
                            () => _host.Llm.OllamaEndpoint);
                        bool reachable = ollama.IsAvailableAsync(maxAttempts: 3).GetAwaiter().GetResult();
                        _ollamaWarmupOk = reachable ? 1 : 0;
                    }
                    catch
                    {
                        _ollamaWarmupOk = 0;
                    }
                }

                sw.Stop();
                DeckleWhispSource.Log.WarmupComplete();
                DeckleWhispSource.Log.WarmupCompleteDetail(
                    sw.ElapsedMilliseconds,
                    MicrophoneWarmupOk,
                    ModelWarmupOk,
                    OllamaWarmupOk);
            }
            catch (Exception ex)
            {
                DeckleWhispSource.Log.WarmupFailed(ex.GetType().Name, ex.Message);
            }
            finally
            {
                _warmupCts = null;
                cts.Dispose();
            }
        });
        thread.IsBackground = true;
        thread.Start();
    }

}

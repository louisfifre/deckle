using Deckle.Audio;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Transcription;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // File audio is independent of the user's dictation domain. An empty
    // PrimingText explicitly suppresses the configured stylistic prompt for
    // this call; otherwise unrelated prompt vocabulary can become the output
    // on quiet or out-of-domain recordings.
    private static readonly TranscriptionContext UnprimedFileContext =
        new(PrimingText: string.Empty);

    // ── FilePipeline partial — the file-transcription capture-less path ─────────
    //
    // Sibling of WorkerRun / ProduceMonolithicAsync (StateMachine + Monolithic
    // partials), for the tray-driven "transcribe a file" feature. It reuses every
    // downstream piece dictation uses — the prime, the backend, the shared
    // FinalizeTranscription — but replaces the microphone producer with a
    // Media-Foundation decode of the chosen file. There is no capture,
    // and (V1) the run is always MONOLITHIC regardless of the user's streaming
    // strategy: a file is transcribed in one backend call, not segmented live.
    //
    // Entry is the engine-owned file queue (StateMachine partial). Its sole
    // consumer CAS'd Idle → Starting and spawned FileWorkerRun. The worker owns
    // the Starting → Transcribing → Idle edge from here.

    // Worker thread body for a file run. Mirrors WorkerRun's shape — create the
    // per-run cancellation tokens, kick the prime off concurrently, run the
    // production, finalize — and shares the exact terminal teardown
    // (SettleWorkerToIdle) so the *→Idle CAS and the idle-unload arming stay
    // identical to the mic path. Runs MTA (a plain background Thread, never STA):
    // Media Foundation, called inside ProduceFileAsync, requires a non-STA thread.
    private void FileWorkerRun()
    {
        using var activity = TranscriptionActivityScope.Open();

        // Same two channels as WorkerRun. _recordCts is the abort signal for the
        // single backend call; a file run has no capture producer to Stop, so it
        // is only ever cancelled by Dispose. _drainCts rides the concurrent prime.
        _recordCts = new CancellationTokenSource();
        _drainCts  = new CancellationTokenSource();

        Task<bool> primeTask = Task.FromResult(true);

        try
        {
            // Warm the model CONCURRENTLY with the decode below, exactly as the
            // mic worker warms it alongside capture. On a cold engine the HUD sits
            // in Charging while the prime loads the model and compiles the kernels;
            // the first real backend call waits on the gate (AwaitPrime) so it
            // never races the prime's dummy inference. A warm engine gets an
            // already-completed gate. The prime rides the drain token — Dispose
            // aborts it, nothing else.
            primeTask = BeginPrime(_drainCts.Token);

            // One id per run under the corpus join contract. A file run does not feed
            // the corpus, but the id is generated uniformly so any future per-run
            // artefact joins the same way.
            _transcriptionId = System.Guid.NewGuid().ToString("N");
            DeckleWhispSource.Log.TranscriptionCorrelation(_transcriptionId);

            PipelineProduction? produced =
                ProduceFileAsync(_recordCts.Token, primeTask).GetAwaiter().GetResult();

            if (produced is not null)
            {
                FinalizeTranscription(produced.Value);
            }
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

    // Decode + single-call transcription of the file. Returns the raw text +
    // buffers for the shared FinalizeTranscription, or null when it already
    // handled an early exit (decode failure, lost CAS, model-not-ready, empty
    // audio, backend failure) and raised Finished itself — the same null-means-
    // handled contract ProduceMonolithicAsync follows.
    //
    // Runs on the file worker thread; producerCt is the run's _recordCts token,
    // the abort signal for the single backend call (Dispose cancels it).
    private async Task<PipelineProduction?> ProduceFileAsync(
        CancellationToken producerCt, Task<bool> primeTask)
    {
        string path = _fileTranscriptionPath ?? "";

        // ── Decode ──────────────────────────────────────────────────────────
        // Media Foundation decode runs synchronously on THIS worker thread — MTA
        // by construction (a background Thread is never STA), which MF requires.
        // Off the UI thread, so the (potentially long) decode never blocks it.
        AudioFileDecodeResult decoded = AudioFileDecoder.Decode(path);
        if (decoded.Status != AudioFileDecodeStatus.Decoded)
        {
            // The Audio provider already logged the Media-Foundation detail; the
            // engine-side anomaly only references the status and surfaces the
            // localized feedback (Replacement — it replaces the HUD's main line).
            var (title, body) = LocalizeDecodeFailure(decoded.Status);
            DeckleWhispSource.Log.FileDecodeFailed();
            DeckleWhispSource.Log.FileDecodeFailedDetail(decoded.Status.ToString());
            EmitUserFeedback(FB_ERROR, title, body, FB_REPLACEMENT);
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        ReadOnlyMemory<float> audio = decoded.Pcm;

        // CAS Starting → Transcribing DIRECTLY — a file run has no Recording or
        // Stopping phase (no capture). Losing this CAS means Dispose
        // won; skip the backend call, same as the monolithic path's lost-CAS
        // branch.
        if (Interlocked.CompareExchange(
                ref _state,
                (int)PipelineState.Transcribing,
                (int)PipelineState.Starting)
            != (int)PipelineState.Starting)
        {
            DeckleWhispSource.Log.TranscribeSkipped(((PipelineState)Volatile.Read(ref _state)).ToString());
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        // ── Inference ───────────────────────────────────────────────────────
        // Prime gate. Never touch the backend before the prime's dummy inference
        // has returned (whisper.cpp is not thread-safe across concurrent calls on
        // one context). "at_stop" is the closest phase label: a cold file run
        // genuinely waits here — there is no capture to overlap the load with.
        //
        // "Transcribing" is deliberately NOT raised before this gate. Unlike the
        // mic path — which shows the chrono the whole take and only reaches this
        // gate post-Stop — a file run has the HUD sitting in Charging (App's
        // ShowPreparing). Holding Charging across a cold model load, and raising
        // "Transcribing" only once the backend is about to run, keeps the state
        // honest: Charging means "getting ready", Transcribing means "working".
        await AwaitPrime(primeTask, "at_stop").ConfigureAwait(false);

        if (!_backend.IsModelLoaded)
        {
            RaiseStatus(Loc.Get("Status_ModelNotReady"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        if (audio.Length == 0)
        {
            DeckleWhispSource.Log.TranscribeEmpty();
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        RaiseStatus(Loc.Get("Status_Transcribing"));

        // No DSP pre-processing. TranscriptionPreprocessor is glossary-scoped to
        // the captured mic signal — it auto-adjusts gain against the user's own
        // recording envelope. A decoded file has no such envelope, so it goes to
        // the backend untouched; RawAudio and BackendAudio are the same buffer.
        TranscriptionResult? consumed = await ConsumeMonolithicAudioAsync(
            audio,
            producerCt,
            UnprimedFileContext).ConfigureAwait(false);
        if (consumed is null)
            return null;

        TranscriptionResult result = consumed;

        // A file transcript is a durable artefact, so partial output is never
        // delivered. In particular, Whisper's repetition guard marks Aborted
        // after detecting a runaway loop; saving those segments produced the
        // identical prompt-derived files observed on 2026-07-31.
        if (!IsFileTranscriptionResultUsable(result))
        {
            DeckleWhispSource.Log.TranscribeFailed();
            DeckleWhispSource.Log.TranscribeFailedDetail(result.ResultCode);
            EmitUserFeedback(FB_ERROR,
                Loc.Get("Engine_TranscriptionFailed_Title"),
                Loc.Get("Engine_TranscriptionFailed_Body"),
                FB_REPLACEMENT);
            RaiseStatus(Loc.Get("Status_TranscriptionFailed"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        string fullText = result.FullText;
        int    nSeg     = result.Segments.Count;

        DeckleWhispSource.Log.TranscribeCompleted();
        DeckleWhispSource.Log.TranscribeCompleteDetail(result.TotalDurationMs, nSeg, fullText.Length);

        if (string.IsNullOrWhiteSpace(fullText))
        {
            RaiseStatus(Loc.Get("Status_Ready"));
            RaiseFinished(TranscriptionOutcome.None);
            return null;
        }

        // RawAudio == BackendAudio: no DSP ran. The corpus is skipped for file
        // runs (see FinalizeTranscription), so these buffers only feed the
        // latency audio_sec metric downstream.
        return new PipelineProduction(
            RawText:           fullText,
            RawAudio:          audio,
            BackendAudio:      audio,
            TotalTranscribeMs: result.TotalDurationMs,
            InitMs:            result.InitDurationMs,
            NSegments:         nSeg);
    }

    internal static bool IsFileTranscriptionResultUsable(TranscriptionResult result) =>
        !result.Aborted && result.ResultCode == 0;

    // AudioFileDecodeStatus → (title, body) for the decode-failure feedback. Every
    // non-Decoded status maps to the same title and its own body; ReadError is the
    // catch-all default so a new status added upstream still surfaces something.
    private static (string Title, string Body) LocalizeDecodeFailure(AudioFileDecodeStatus status)
    {
        string body = status switch
        {
            AudioFileDecodeStatus.FileNotFound      => Loc.Get("FileTranscription_DecodeFailed_Body_FileNotFound"),
            AudioFileDecodeStatus.UnsupportedFormat => Loc.Get("FileTranscription_DecodeFailed_Body_UnsupportedFormat"),
            AudioFileDecodeStatus.NoAudioTrack      => Loc.Get("FileTranscription_DecodeFailed_Body_NoAudioTrack"),
            AudioFileDecodeStatus.ProtectedContent  => Loc.Get("FileTranscription_DecodeFailed_Body_ProtectedContent"),
            _                                       => Loc.Get("FileTranscription_DecodeFailed_Body_ReadError"),
        };
        return (Loc.Get("FileTranscription_DecodeFailed_Title"), body);
    }

    // Size of the source file for the start-of-run Verbose line. Best-effort:
    // any failure to stat the file returns 0 rather than derailing a run that
    // will decode the file for real a moment later.
    private static long SafeFileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}

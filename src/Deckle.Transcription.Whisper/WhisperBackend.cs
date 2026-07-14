using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Deckle.Core;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Transcription.Whisper;

// ── WhisperBackend ───────────────────────────────────────────────────────────
//
// IAsrBackend implementation backed by whisper.cpp through the WhisperPInvoke
// surface. Encapsulates every P/Invoke detail and every whisper.cpp idiom
// (native log callback, segment callback) so the orchestrator in
// Deckle.Transcription only deals with the IAsrBackend contract.
//
// Threading. LoadModelAsync runs synchronously inside Task.Run-friendly code
// (whisper_init is blocking); the orchestrator calls it from a background
// worker. TranscribeAsync wraps the synchronous whisper_full behind an async
// signature — future backends with real HTTP/IPC plumbing will fill the
// signature with actual asynchrony. UnloadModel can fire from a timer at
// any time; the model lock prevents it from racing an in-flight Transcribe.
//
// Lifetime. The static whisper_log_set callback is process-wide; we install
// it once at construction and never reset it (whisper.cpp keeps the function
// pointer indefinitely; clearing it would have to coordinate with any other
// libwhisper consumer in the process, which today does not exist). The
// segment + abort callbacks are per-call, kept rooted in instance fields
// for the duration of whisper_full.
public sealed class WhisperBackend : IAsrBackend
{
    public string Name => "whisper";

    private readonly ITranscriptionEngineHost _host;
    private readonly object _modelLock = new();
    // Serialises whisper_full calls on the same _ctx. The orchestrator keeps the
    // prime's dummy inference and the real transcription from overlapping at a
    // higher level — the prime now runs on its own thread, concurrently with the
    // capture, and the engine gates the first real call behind it (AwaitPrime) —
    // so no concurrent caller is expected in practice. The lock is the hard
    // backend-local guard underneath that: whisper.cpp is not thread-safe across
    // concurrent calls on a single context (a native segfault no managed handler
    // can rescue), and the IAsrBackend contract must not assume its caller stays
    // serialised forever.
    private readonly object _transcribeLock = new();

    // volatile: prevents the JIT from caching this in a register so a
    // background thread sees the real handle, not a stale snapshot.
    private volatile IntPtr _ctx = IntPtr.Zero;
    private volatile string _detectedBackend = "CPU";
    private bool _disposed;

    // Callback storage — keeps the managed delegate rooted while whisper.cpp
    // holds its function pointer. Setting to null would let the GC reclaim
    // the underlying thunk on the next collection, producing a native crash
    // the next time whisper invokes the callback.
    private WhisperPInvoke.WhisperLogCallback? _logCallback;
    private WhisperNewSegmentCallback? _segmentCallback;
    private WhisperAbortCallback? _abortCallback;

    // Init-phase log compactor — owns the per-phase string state machine that
    // consolidates whisper.cpp's noisy init lines into one event per phase.
    private readonly WhisperNativeLogCompactor _logCompactor = new();

    // ── IAsrBackend surface ──────────────────────────────────────────────────

    public bool IsModelLoaded => _ctx != IntPtr.Zero;
    public string? DetectedAccelerator => _ctx == IntPtr.Zero ? null : _detectedBackend;

    public WhisperBackend(ITranscriptionEngineHost host)
    {
        _host = host;
        InstallWhisperLogHook();
    }

    public Task<ModelLoadResult> LoadModelAsync(CancellationToken ct)
    {
        // whisper_init_from_file_with_params is a blocking native call. We
        // expose an async signature for parity with backends that may have
        // real asynchrony (HTTP, IPC) and wrap the sync result here.
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(LoadModelSync());
    }

    private ModelLoadResult LoadModelSync()
    {
        lock (_modelLock)
        {
            if (_ctx != IntPtr.Zero)
            {
                // Already loaded — caller should have checked IsModelLoaded
                // first; treat as success with 0 ms to keep the contract clean.
                return new ModelLoadResult(true, 0, _detectedBackend, null);
            }

            string modelPath = ResolveModelPath();

            if (!File.Exists(modelPath))
            {
                DeckleWhispSource.Log.ModelLoadAborted();
                DeckleWhispSource.Log.ModelLoadAbortedDetail("file_not_found", modelPath);
                return new ModelLoadResult(false, 0, null, "file_not_found");
            }

            double fileMb = new FileInfo(modelPath).Length / 1024.0 / 1024.0;
            string basename = Path.GetFileName(modelPath);
            DeckleWhispSource.Log.ModelLoading();
            DeckleWhispSource.Log.ModelLoadStart(basename, fileMb);

            // Reset the backend detection before init so a re-load picks up
            // the current backend rather than the one detected on a previous
            // load. The log hook overwrites this field synchronously during
            // init as soon as it sees a ggml_vulkan: / cuda / metal prefix;
            // CPU stays as the fallback when no GPU backend initialises.
            _detectedBackend = "CPU";

            var sw = Stopwatch.StartNew();
            IntPtr ctxParamsPtr = WhisperPInvoke.whisper_context_default_params_by_ref();
            WhisperContextParams ctxParams = Marshal.PtrToStructure<WhisperContextParams>(ctxParamsPtr);
            WhisperPInvoke.whisper_free_context_params(ctxParamsPtr);
            ctxParams.use_gpu = 1;

            _ctx = WhisperPInvoke.whisper_init_from_file_with_params(modelPath, ctxParams);
            sw.Stop();
            DeckleWhispSource.Log.ModelInitFromFile((long)_ctx);

            if (_ctx == IntPtr.Zero)
            {
                DeckleWhispSource.Log.ModelLoadFailed();
                DeckleWhispSource.Log.ModelLoadFailedDetail(modelPath);
                return new ModelLoadResult(false, sw.ElapsedMilliseconds, null, "init_failed");
            }

            DeckleWhispSource.Log.ModelLoaded();
            DeckleWhispSource.Log.ModelLoadedDetail(_detectedBackend);
            DeckleWhispSource.Log.ModelLoadComplete(sw.ElapsedMilliseconds, _detectedBackend);

            return new ModelLoadResult(true, sw.ElapsedMilliseconds, _detectedBackend, null);
        }
    }

    public void UnloadModel()
    {
        lock (_modelLock)
        {
            if (_ctx == IntPtr.Zero) return;
            WhisperPInvoke.whisper_free(_ctx);
            _ctx = IntPtr.Zero;
            DeckleWhispSource.Log.ModelUnloadedJalon();
        }
    }

    public Task<TranscriptionResult> TranscribeAsync(
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct,
        TranscriptionContext? context = null)
    {
        return Task.FromResult(TranscribeSync(pcmSamples, segmentSink, ct, context));
    }

    // ── Model path resolution ────────────────────────────────────────────────

    // Order of precedence:
    //   1. DECKLE_MODEL_PATH env var if it points to an absolute existing path.
    //   2. host.Whisp.Engine.Model (user setting), fallback to the
    //      Whisper catalog's default, joined with host.ResolveModelsDirectory().
    //   3. If that file is absent, the best catalog model actually installed —
    //      an install carrying only large-v3 must survive the ggml-base default
    //      bump without a 3 GB re-download or a dead engine.
    private string ResolveModelPath()
    {
        string modelsDirectory = _host.ResolveModelsDirectory();
        string? envPath = Environment.GetEnvironmentVariable("DECKLE_MODEL_PATH");
        SpeechModelResolution resolution = SpeechModelResolver.ResolvePath(
            _host.Transcription.Engine.Model,
            modelsDirectory,
            envPath,
            SpeechModels.IsUsableModelFile);

        if (resolution.InstalledFallbackFileName is { } installed)
        {
            DeckleWhispSource.Log.ModelFallback();
            DeckleWhispSource.Log.ModelFallbackDetail(
                resolution.ConfiguredFileName,
                installed);
        }

        if (resolution.IgnoredEnvironmentPath is { } ignored)
        {
            DeckleWhispSource.Log.ModelPathEnvIgnored();
            DeckleWhispSource.Log.ModelPathEnvIgnoredDetail(ignored, resolution.Path);
        }
        return resolution.Path;
    }

    // ── Transcribe internals ─────────────────────────────────────────────────

    // whisper.cpp new_segment_callback signature:
    //   void fn(whisper_context* ctx, whisper_state* state, int n_new, void* user_data)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WhisperNewSegmentCallback(IntPtr ctx, IntPtr state, int n_new, IntPtr user_data);

    // whisper.cpp abort_callback signature: bool fn(void* user_data). Returning
    // true requests a clean stop — whisper_full returns 0 with the segments
    // emitted so far. Used as the kill switch for the repetition-loop detector
    // and as the bridge for CancellationToken.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool WhisperAbortCallback(IntPtr user_data);

    // Per-call accumulation state. Reset at the top of TranscribeSync.
    private readonly List<TranscriptionSegment> _segmentsLocal = new();
    private readonly object _segmentsLock = new();
    private Action<TranscriptionSegment>? _segmentSink;
    private volatile bool _abortRequested;
    private int _tokenBeg;
    private Stopwatch? _transcribeSw;
    private string _strategyLabel = "";
    // Where the current call's audio sits on the whole recording's timeline,
    // from TranscriptionContext.TimelineOffsetSec. Added to the per-segment
    // t0/t1 we log so a streaming segment reads its true position in the take.
    // 0 for a standalone (monolithic) call.
    private double _timelineOffsetSec;
    private readonly RepetitionDetector _repetitionDetector = new();

    private TranscriptionResult TranscribeSync(
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct,
        TranscriptionContext? context = null)
    {
        IntPtr ctx = _ctx;
        if (ctx == IntPtr.Zero)
        {
            return new TranscriptionResult(
                Array.Empty<TranscriptionSegment>(), "", 0, 0, false, -1);
        }
        if (pcmSamples.Length == 0)
        {
            DeckleWhispSource.Log.TranscribeEmpty();
            return new TranscriptionResult(
                Array.Empty<TranscriptionSegment>(), "", 0, 0, false, 0);
        }

        // Reset per-call accumulators before any callback may fire.
        lock (_segmentsLock) _segmentsLocal.Clear();
        _segmentSink = segmentSink;
        _abortRequested = false;
        _repetitionDetector.Reset();

        IntPtr fullParamsPtr = WhisperPInvoke.whisper_full_default_params_by_ref(0);
        WhisperFullParams wparams = Marshal.PtrToStructure<WhisperFullParams>(fullParamsPtr);
        WhisperPInvoke.whisper_free_params(fullParamsPtr);

        wparams.print_progress = 0;

        var TranscriptionSettings = _host.Transcription;
        WhisperParamsMapper.NativeAllocations nativeAllocs =
            WhisperParamsMapper.Apply(ref wparams, TranscriptionSettings, _host.ResolveModelsDirectory(), context?.PrimingText);

        _tokenBeg = WhisperPInvoke.whisper_token_beg(ctx);

        _segmentCallback = OnNewSegment;
        wparams.new_segment_callback = Marshal.GetFunctionPointerForDelegate(_segmentCallback);
        wparams.new_segment_callback_user_data = IntPtr.Zero;

        // Combined abort signal: repetition guard OR external cancellation.
        _abortCallback = _ => _abortRequested || ct.IsCancellationRequested;
        wparams.abort_callback = Marshal.GetFunctionPointerForDelegate(_abortCallback);
        wparams.abort_callback_user_data = IntPtr.Zero;

        float audioSec = (float)pcmSamples.Length / 16_000f;
        _strategyLabel = wparams.strategy == 1 ? $"beam{wparams.beam_search_beam_size}" : "greedy";
        _timelineOffsetSec = context?.TimelineOffsetSec ?? 0;

        // One-time configuration preamble. A standalone call (monolithic) and the
        // first utterance of a streaming take log it; the rest skip it, so a long
        // streaming dictation shows the params/prompt once, not once per utterance
        // (they are identical across the take). Pure observability gate — defaults
        // on when there is no context.
        if (context?.EmitPreamble ?? true)
        {
            DeckleWhispSource.Log.TranscribeStarted();
            DeckleWhispSource.Log.TranscribeStartDetail(audioSec, pcmSamples.Length, _strategyLabel);
            string strategyVerbose = wparams.strategy == 1
                ? $"beam(size={wparams.beam_search_beam_size})"
                : "greedy";
            DeckleWhispSource.Log.TranscribeParams(
                $"strategy={strategyVerbose} | temp={wparams.temperature:F2}+{wparams.temperature_inc:F2}" +
                $" | logprob_thold={wparams.logprob_thold:F2} | entropy_thold={wparams.entropy_thold:F2}" +
                $" | no_speech_thold={wparams.no_speech_thold:F2} | suppress_nst={wparams.suppress_nst}" +
                $" | carry_prompt={wparams.carry_initial_prompt} | n_threads={wparams.n_threads}");

            string prompt = context?.PrimingText ?? TranscriptionSettings.Engine.InitialPrompt;
            bool carry = TranscriptionSettings.Engine.CarryInitialPrompt;
            if (!string.IsNullOrEmpty(prompt))
            {
                string truncated = prompt.Length > 60 ? prompt[..60] + "…" : prompt;
                DeckleWhispSource.Log.TranscribePrompt(prompt.Length, carry, truncated);
            }
        }

        _transcribeSw = Stopwatch.StartNew();
        _whisperInitSw = Stopwatch.StartNew();

        int result;
        float[] audioArray;
        int sampleCount = pcmSamples.Length;
        if (MemoryMarshal.TryGetArray(pcmSamples, out ArraySegment<float> segment)
            && segment.Offset == 0)
        {
            audioArray = segment.Array!;
        }
        else
        {
            audioArray = pcmSamples.ToArray();
        }
        lock (_transcribeLock)
        {
            result = WhisperPInvoke.whisper_full(ctx, wparams, audioArray, sampleCount);
        }
        _transcribeSw.Stop();
        long totalMs = _transcribeSw.ElapsedMilliseconds;

        if (_whisperInitSw is { IsRunning: true }) _whisperInitSw.Stop();
        long whisperInitMs = _whisperInitSw?.ElapsedMilliseconds ?? 0;

        nativeAllocs.Free();

        bool aborted = _abortRequested || ct.IsCancellationRequested;

        if (result != 0 && !_abortRequested)
        {
            DeckleWhispSource.Log.TranscribeFailed();
            DeckleWhispSource.Log.TranscribeFailedDetail(result);
            return new TranscriptionResult(
                Array.Empty<TranscriptionSegment>(), "", totalMs, whisperInitMs, aborted, result);
        }

        // Snapshot segments accumulated by the callback. Build full text by
        // concatenating segment texts and trimming the outer whitespace.
        TranscriptionSegment[] segments;
        lock (_segmentsLock)
        {
            segments = _segmentsLocal.ToArray();
        }
        var sb = new System.Text.StringBuilder();
        foreach (var seg in segments) sb.Append(seg.Text);
        string fullText = sb.ToString().Trim();

        // Known-hallucination filter. On silence/music whisper emits a fixed
        // subtitle-credit phrase from its training corpus at high confidence,
        // where neither the confidence thresholds nor the repetition guard bite.
        // Matched on the WHOLE utterance (never a substring), so a real dictation
        // quoting the phrase is untouched. A hit blanks the text — the segments
        // stay for telemetry, and an empty FullText is already treated as "no
        // text" by both the streaming consumer and the monolithic finalize.
        if (KnownHallucinations.Matches(fullText))
        {
            string preview = fullText.Length > 60 ? fullText[..60] + "…" : fullText;
            DeckleWhispSource.Log.TranscribeHallucinationFiltered();
            DeckleWhispSource.Log.TranscribeHallucinationFilteredDetail(preview);
            fullText = "";
        }

        return new TranscriptionResult(segments, fullText, totalMs, whisperInitMs, aborted, result);
    }

    private void OnNewSegment(IntPtr ctx, IntPtr state, int n_new, IntPtr user_data)
    {
        try
        {
            int total = WhisperPInvoke.whisper_full_n_segments(ctx);
            int from = total - n_new;
            int tokenBeg = _tokenBeg;
            for (int i = from; i < total; i++)
            {
                string segText = Marshal.PtrToStringUTF8(
                    WhisperPInvoke.whisper_full_get_segment_text(ctx, i)) ?? "";
                long t0 = WhisperPInvoke.whisper_full_get_segment_t0(ctx, i);
                long t1 = WhisperPInvoke.whisper_full_get_segment_t1(ctx, i);
                float nsp = WhisperPInvoke.whisper_full_get_segment_no_speech_prob(ctx, i);

                // Per-segment confidence over text tokens only — timestamp
                // tokens (id >= tokenBeg) are excluded from the average.
                int nTok = WhisperPInvoke.whisper_full_n_tokens(ctx, i);
                float sumP = 0f, minP = 1f;
                int textTok = 0;
                for (int k = 0; k < nTok; k++)
                {
                    int id = WhisperPInvoke.whisper_full_get_token_id(ctx, i, k);
                    if (id >= tokenBeg) continue;
                    float p = WhisperPInvoke.whisper_full_get_token_p(ctx, i, k);
                    sumP += p;
                    if (p < minP) minP = p;
                    textTok++;
                }
                float avgP = textTok > 0 ? sumP / textTok : 0f;
                if (textTok == 0) minP = 0f;

                var segment = new TranscriptionSegment(segText, t0, t1, avgP, nsp);
                lock (_segmentsLock) _segmentsLocal.Add(segment);

                // Repetition-loop guard: if recent segments repeat — one phrase
                // (A A A) or an alternating pair (A B A B) — ask whisper to stop.
                // abort_callback is probed between decoder steps, so one more
                // segment may surface — that's expected.
                if (!_abortRequested &&
                    _repetitionDetector.ObserveAndShouldAbort(segText, out int streak, out int period))
                {
                    _abortRequested = true;
                    string preview = segText.Trim();
                    if (preview.Length > 60) preview = preview[..60] + "…";
                    DeckleWhispSource.Log.TranscribeRepetitionLoop();
                    DeckleWhispSource.Log.TranscribeRepetitionLoopDetail(streak, period, preview);
                }

                _segmentSink?.Invoke(segment);

                // Fixed-width columns so the text column lines up vertically and a
                // take reads top-to-bottom at a glance. t0/t1 are offset onto the
                // whole recording's timeline (_timelineOffsetSec) so each segment
                // shows its true position in the take, not a per-call zero; in
                // monolithic the offset is 0 → absolute-from-start as before.
                double t0Sec = _timelineOffsetSec + t0 / 100.0;
                double t1Sec = _timelineOffsetSec + t1 / 100.0;
                double dur = (t1 - t0) / 100.0;
                string trimmed = segText.Trim();
                DeckleWhispSource.Log.SegmentEmitted(
                    $"seg #{i + 1,2} | {t0Sec,7:F1}→{t1Sec,7:F1}s | dur {dur,5:F1}s" +
                    $" | nsp {nsp,4:P0} | p̄ {avgP:F2} | min {minP:F2} | tok {textTok,2}/{nTok,2}" +
                    $" | \"{trimmed}\"");
            }
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.SegmentCallbackThrew();
            DeckleWhispSource.Log.SegmentCallbackThrewDetail(ex.GetType().Name, ex.Message);
        }
    }

    // ── Whisper native log hook ──────────────────────────────────────────────
    //
    // Redirects whisper.cpp internal logs (ggml_log) to the LogWindow. Handles
    // two concerns at once:
    //
    //   1. Backend detection — the first ggml_vulkan: / ggml_cuda: / ggml_metal:
    //      prefix wins and is captured into _detectedBackend.
    //   2. Init-phase compaction — four phases of whisper.cpp init each emit
    //      ~3 to 17 separate lines; we accumulate per-phase values and emit
    //      one consolidated event per phase as soon as the prefix changes.
    //
    // The hook is installed once at construction. whisper_log_set keeps the
    // function pointer indefinitely — clearing it would have to coordinate
    // with any other libwhisper consumer in the process.
    private void InstallWhisperLogHook()
    {
        _logCallback = (level, textPtr, _) =>
        {
            try
            {
                string msg = Marshal.PtrToStringUTF8(textPtr)?.TrimEnd('\r', '\n', ' ') ?? "";
                if (string.IsNullOrEmpty(msg)) return;

                // ── Backend detection (first hit wins, sticks) ───────────
                if (_detectedBackend == "CPU")
                {
                    if (msg.StartsWith("ggml_vulkan:", StringComparison.Ordinal))
                        _detectedBackend = "Vulkan";
                    else if (msg.StartsWith("ggml_cuda_init:", StringComparison.Ordinal) ||
                             msg.StartsWith("ggml_cuda:", StringComparison.Ordinal))
                        _detectedBackend = "CUDA";
                    else if (msg.StartsWith("ggml_metal_init:", StringComparison.Ordinal) ||
                             msg.StartsWith("ggml_metal:", StringComparison.Ordinal))
                        _detectedBackend = "Metal";
                }

                // ── Init-phase compaction ────────────────────────────────
                // Each phase prefix accumulates its own values; the moment a
                // different prefix is seen, the accumulated phase is flushed
                // as a single event before the new phase starts. Lines that
                // are not from a tracked phase flush any pending phase first,
                // then fall through to the level switch below.
                if (_logCompactor.TryAccumulatePhaseLine(msg)) return;

                // ── "no GPU found" downgrade for the second backend init ─
                // The VAD context creation triggers a second whisper_backend
                // _init_gpu that always reports "no GPU found" (whisper.cpp
                // hardcodes use_gpu=false in whisper_vad_init_context). Benign
                // but alarming at Warn — keep it Verbose. Targeted match so a
                // real GPU failure phrased differently still surfaces.
                if (msg.StartsWith("whisper_backend_init_gpu", StringComparison.Ordinal) &&
                    msg.IndexOf("no GPU found", StringComparison.Ordinal) >= 0)
                {
                    DeckleWhispSource.Log.WhisperLogVerbose(msg);
                    return;
                }

                // ggml levels: 0=None, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Cont.
                switch (level)
                {
                    case 4:
                        DeckleWhispSource.Log.WhisperLogError();
                        DeckleWhispSource.Log.WhisperLogErrorDetail(msg);
                        break;
                    case 3:
                        DeckleWhispSource.Log.WhisperLogWarning();
                        DeckleWhispSource.Log.WhisperLogWarningDetail(msg);
                        break;
                    default: DeckleWhispSource.Log.WhisperLogVerbose(msg); break;
                }
            }
            catch
            {
                // Never let an exception cross the native boundary.
            }
        };

        try
        {
            WhisperPInvoke.whisper_log_set(_logCallback, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.WhisperLogSetUnavailable();
            DeckleWhispSource.Log.WhisperLogSetUnavailableDetail(ex.Message);
        }
    }

    // ── Init-phase timing ─────────────────────────────────────────────────────
    //
    // Wall-clock for whisper.cpp's pre-decode setup: started just before
    // whisper_full and stopped when it returns. Surfaced as whisper_init_ms.
    private Stopwatch? _whisperInitSw;

    // ── Dispose ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnloadModel();
        // Drop the rooted callbacks — Dispose implies no further invocations
        // from native code. The log callback is process-global; clearing the
        // pointer here is intentional (no other libwhisper consumer expects it).
        _segmentCallback = null;
        _abortCallback = null;
        _logCallback = null;
    }
}

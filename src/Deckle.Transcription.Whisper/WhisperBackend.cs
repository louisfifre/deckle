using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Deckle.Core;
using Deckle.Transcription;
using Deckle.Transcription.Engine;
using Deckle.Transcription.Whisper.Engine;
using Deckle.Transcription.Whisper.Pinvoke;
using Deckle.Transcription.Whisper.Setup;

namespace Deckle.Transcription.Whisper;

// ── WhisperBackend ───────────────────────────────────────────────────────────
//
// IAsrBackend implementation backed by whisper.cpp through the WhisperPInvoke
// surface. Encapsulates every P/Invoke detail and every whisper.cpp idiom
// (native log callback, segment callback, VAD log parsing) so the orchestrator
// in Deckle.Transcription only deals with the IAsrBackend contract.
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
    // Serialises whisper_full calls on the same _ctx. The state machine in
    // the orchestrator already prevents concurrent user-driven transcriptions,
    // but Warmup() calls Transcribe on a separate thread at startup and a
    // hotkey landing during that window would race the warmup whisper_full
    // on the same context. whisper.cpp is not thread-safe across concurrent
    // calls on a single context — a native segfault that no managed handler
    // can rescue. This lock keeps the invariant local to the backend.
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
                DeckleWhispSource.Log.ModelLoadAborted("file_not_found", modelPath);
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
                DeckleWhispSource.Log.ModelLoadFailed(modelPath);
                return new ModelLoadResult(false, sw.ElapsedMilliseconds, null, "init_failed");
            }

            DeckleWhispSource.Log.ModelLoaded(_detectedBackend);
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
        CancellationToken ct)
    {
        return Task.FromResult(TranscribeSync(pcmSamples, segmentSink, ct));
    }

    // ── Model path resolution ────────────────────────────────────────────────

    // Order of precedence:
    //   1. DECKLE_MODEL_PATH env var if it points to an absolute existing path.
    //   2. host.Whisp.Engine.Model (user setting), fallback to the
    //      Whisper catalog's default, joined with host.ResolveModelsDirectory().
    private string ResolveModelPath()
    {
        var engine = _host.Transcription.Engine;
        string modelFile = string.IsNullOrWhiteSpace(engine.Model)
            ? SpeechModels.DefaultModelFileName
            : engine.Model;
        string fallback = Path.Combine(_host.ResolveModelsDirectory(), modelFile);

        string? envPath = Environment.GetEnvironmentVariable("DECKLE_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(envPath)) return fallback;
        if (!Path.IsPathRooted(envPath) || !File.Exists(envPath))
        {
            DeckleWhispSource.Log.ModelPathEnvIgnored(envPath, fallback);
            return fallback;
        }
        return envPath;
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
    private long _lastSegmentT1;
    private Stopwatch? _transcribeSw;
    private string _strategyLabel = "";
    private readonly RepetitionDetector _repetitionDetector = new();

    private TranscriptionResult TranscribeSync(
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct)
    {
        IntPtr ctx = _ctx;
        if (ctx == IntPtr.Zero)
        {
            return new TranscriptionResult(
                Array.Empty<TranscriptionSegment>(), "", 0, 0, 0, false, -1);
        }
        if (pcmSamples.Length == 0)
        {
            DeckleWhispSource.Log.TranscribeEmpty();
            return new TranscriptionResult(
                Array.Empty<TranscriptionSegment>(), "", 0, 0, 0, false, 0);
        }

        // Reset per-call accumulators before any callback may fire.
        lock (_segmentsLock) _segmentsLocal.Clear();
        _segmentSink = segmentSink;
        _abortRequested = false;
        _lastSegmentT1 = -1;
        _repetitionDetector.Reset();
        ResetVadParsingState();

        IntPtr fullParamsPtr = WhisperPInvoke.whisper_full_default_params_by_ref(0);
        WhisperFullParams wparams = Marshal.PtrToStructure<WhisperFullParams>(fullParamsPtr);
        WhisperPInvoke.whisper_free_params(fullParamsPtr);

        wparams.print_progress = 0;

        var TranscriptionSettings = _host.Transcription;
        WhisperParamsMapper.NativeAllocations nativeAllocs =
            WhisperParamsMapper.Apply(ref wparams, TranscriptionSettings, _host.ResolveModelsDirectory());

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

        string prompt = TranscriptionSettings.Engine.InitialPrompt;
        bool carry = TranscriptionSettings.Engine.CarryInitialPrompt;
        if (!string.IsNullOrEmpty(prompt))
        {
            string truncated = prompt.Length > 60 ? prompt[..60] + "…" : prompt;
            DeckleWhispSource.Log.TranscribePrompt(prompt.Length, carry, truncated);
        }

        _vadCapturing = true;
        _transcribeSw = Stopwatch.StartNew();
        _whisperInitSw = Stopwatch.StartNew();

        int result;
        // whisper_full's pcmSamples parameter is `float[]` in the P/Invoke
        // signature. ReadOnlyMemory.ToArray() is a defensive copy — we accept
        // the allocation here to keep the IAsrBackend signature non-leaky
        // (the orchestrator doesn't have to hand us a raw array).
        float[] audioArray = pcmSamples.ToArray();
        lock (_transcribeLock)
        {
            result = WhisperPInvoke.whisper_full(ctx, wparams, audioArray, audioArray.Length);
        }
        _transcribeSw.Stop();
        long totalMs = _transcribeSw.ElapsedMilliseconds;

        _vadCapturing = false;
        if (_vadSw is { IsRunning: true }) _vadSw.Stop();
        if (_whisperInitSw is { IsRunning: true }) _whisperInitSw.Stop();
        long vadMs = _vadSw?.ElapsedMilliseconds ?? 0;
        long whisperInitMs = _whisperInitSw?.ElapsedMilliseconds ?? 0;

        // No-speech short-circuit fallback — whisper.cpp bails before the
        // "Reduced audio from" marker when VAD finds 0 segments, so the hook
        // never closes the cycle. Force-emit here so the consolidated Verbose
        // line still surfaces.
        if (_vadSw is not null && !_vadEnded)
        {
            _vadEnded = true;
            if (_vadSegments < 0) _vadSegments = 0;
            EmitVadSummary(_vadSw.Elapsed.TotalSeconds);
        }

        nativeAllocs.Free();

        bool aborted = _abortRequested || ct.IsCancellationRequested;

        if (result != 0 && !_abortRequested)
        {
            DeckleWhispSource.Log.TranscribeFailed(result);
            return new TranscriptionResult(
                Array.Empty<TranscriptionSegment>(), "", totalMs, whisperInitMs, vadMs, aborted, result);
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

        return new TranscriptionResult(segments, fullText, totalMs, whisperInitMs, vadMs, aborted, result);
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

                // Repetition-loop guard: if the last N segments are identical,
                // ask whisper to stop. abort_callback is probed between decoder
                // steps, so one more segment may surface — that's expected.
                if (!_abortRequested &&
                    _repetitionDetector.ObserveAndShouldAbort(segText, out int streak))
                {
                    _abortRequested = true;
                    string preview = segText.Trim();
                    if (preview.Length > 60) preview = preview[..60] + "…";
                    DeckleWhispSource.Log.TranscribeRepetitionLoop(streak, preview);
                }

                _segmentSink?.Invoke(segment);

                double dur = (t1 - t0) / 100.0;
                double gap = _lastSegmentT1 < 0 ? 0.0 : (t0 - _lastSegmentT1) / 100.0;
                _lastSegmentT1 = t1;
                double elapsedSec = _transcribeSw?.Elapsed.TotalSeconds ?? 0;
                string trimmed = segText.Trim();
                DeckleWhispSource.Log.SegmentEmitted(
                    $"seg #{i + 1} | t0={t0 / 100.0:F1}s | t1={t1 / 100.0:F1}s" +
                    $" | dur={dur:F1}s | gap={(gap >= 0 ? "+" : "")}{gap:F1}s" +
                    $" | nsp={nsp:P0} | p̄={avgP:F2} | min={minP:F2}" +
                    $" | tok={textTok}/{nTok} | elapsed={elapsedSec:F1}s | text=\"{trimmed}\"");
            }
        }
        catch (Exception ex)
        {
            DeckleWhispSource.Log.SegmentCallbackThrew(ex.GetType().Name, ex.Message);
        }
    }

    // ── Whisper native log hook ──────────────────────────────────────────────
    //
    // Redirects whisper.cpp internal logs (ggml_log) to the LogWindow. Handles
    // three concerns at once:
    //
    //   1. Backend detection — the first ggml_vulkan: / ggml_cuda: / ggml_metal:
    //      prefix wins and is captured into _detectedBackend.
    //   2. VAD parsing — whisper.cpp's VAD module emits per-segment chatter
    //      that would flood the log; we accumulate the relevant numbers and
    //      emit a single VadParsed summary line at the end of the cycle.
    //   3. Init-phase compaction — four phases of whisper.cpp init each emit
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

                // ── VAD parsing (suppress raw lines, emit summary) ───────
                if (_vadCapturing && msg.StartsWith("whisper_vad", StringComparison.Ordinal))
                {
                    AccumulateVadLine(msg);
                    if (!_vadEnded && msg.IndexOf("Reduced audio from", StringComparison.Ordinal) >= 0)
                    {
                        _vadSw?.Stop();
                        _vadEnded = true;
                        EmitVadSummary(_vadSw?.Elapsed.TotalSeconds ?? 0);
                    }
                    return;
                }

                // ── Init-phase compaction ────────────────────────────────
                // Each phase prefix accumulates its own values; the moment a
                // different prefix is seen, the accumulated phase is flushed
                // as a single event before the new phase starts. Lines that
                // are not from a tracked phase flush any pending phase first,
                // then fall through to the level switch below.
                if (TryAccumulatePhaseLine(msg)) return;

                // ── "no GPU found" downgrade for the second backend init ─
                // The VAD context creation triggers a second whisper_backend
                // _init_gpu that always reports "no GPU found" (whisper.cpp
                // hardcodes use_gpu=false in whisper_vad_init_context). Bénin
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
                    case 4: DeckleWhispSource.Log.WhisperLogError(msg); break;
                    case 3: DeckleWhispSource.Log.WhisperLogWarning(msg); break;
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
            DeckleWhispSource.Log.WhisperLogSetUnavailable(ex.Message);
        }
    }

    // ── Init-phase compaction ────────────────────────────────────────────────
    //
    // whisper.cpp's init flow emits four distinct prefix groups whose lines
    // are useful as a whole but noisy individually. We accumulate the
    // value-of-interest from each line of the active phase into per-phase
    // state, and flush a single consolidated event the moment we see a line
    // from a different phase (or any non-phase line). The fifth init prefix —
    // whisper_init_from_file_with_params_no_state: — emits a single line and
    // passes through unchanged.

    private static readonly string[] s_phasePrefixes = new[]
    {
        "whisper_init_with_params_no_state:",
        "whisper_model_load:",
        "whisper_backend_init_gpu:",
        "whisper_init_state:",
    };

    // Current phase index (0..3) and accumulator. -1 means no phase active.
    private int _phaseIndex = -1;
    private readonly List<string> _phaseAccumulator = new();

    // Returns true when the line was consumed by the phase machinery (and so
    // must not flow through the normal level switch in the log hook).
    private bool TryAccumulatePhaseLine(string msg)
    {
        int matched = -1;
        for (int i = 0; i < s_phasePrefixes.Length; i++)
        {
            if (msg.StartsWith(s_phasePrefixes[i], StringComparison.Ordinal))
            {
                matched = i;
                break;
            }
        }

        if (matched < 0)
        {
            // Non-phase line — flush any pending phase first, then let the
            // caller route the line normally.
            FlushPendingPhase();
            return false;
        }

        if (_phaseIndex >= 0 && matched != _phaseIndex)
        {
            // Different phase started — flush the previous one before
            // starting accumulation on the new phase.
            FlushPendingPhase();
        }

        _phaseIndex = matched;
        // Capture the substring after the prefix, trimmed. Empty bodies are
        // skipped — they carry no value to consolidate.
        string body = msg.Substring(s_phasePrefixes[matched].Length).Trim();
        if (body.Length > 0) _phaseAccumulator.Add(body);
        return true;
    }

    private void FlushPendingPhase()
    {
        if (_phaseIndex < 0) return;
        string consolidated = string.Join(" | ", _phaseAccumulator);
        switch (_phaseIndex)
        {
            case 0: DeckleWhispSource.Log.WhisperInitParamsParsed(consolidated); break;
            case 1: DeckleWhispSource.Log.WhisperModelLoadParsed(consolidated); break;
            case 2: DeckleWhispSource.Log.WhisperBackendInitParsed(consolidated); break;
            case 3: DeckleWhispSource.Log.WhisperInitStateParsed(consolidated); break;
        }
        _phaseAccumulator.Clear();
        _phaseIndex = -1;
    }

    // ── VAD parsing ──────────────────────────────────────────────────────────

    private Stopwatch? _vadSw;
    private Stopwatch? _whisperInitSw;
    private volatile bool _vadCapturing;
    private bool _vadEnded;
    private float _vadSpeechSec = -1f;
    private int _vadSegments = -1;
    private float _vadReductionPct = -1f;
    private float _vadInferenceMs = -1f;
    private int _vadMappingPoints = -1;

    private static readonly Regex s_vadSpeechRegex = new(
        @"total duration of speech segments:\s*([\d.]+)\s*s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex s_vadSegmentsRegex = new(
        @"detected\s+(\d+)\s+speech segments",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex s_vadReductionRegex = new(
        @"\(([\d.]+)%\s*reduction\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex s_vadInferenceRegex = new(
        @"vad time\s*=\s*([\d.]+)\s*ms",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex s_vadMappingRegex = new(
        @"mapping table with\s+(\d+)\s+points",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private void ResetVadParsingState()
    {
        _vadSw = null;
        _whisperInitSw = null;
        _vadEnded = false;
        _vadSpeechSec = -1f;
        _vadSegments = -1;
        _vadReductionPct = -1f;
        _vadInferenceMs = -1f;
        _vadMappingPoints = -1;
    }

    private void AccumulateVadLine(string msg)
    {
        if (_vadSw is null)
        {
            _vadSw = Stopwatch.StartNew();
            // First VAD line is also the cue that whisper_init pre-VAD setup
            // is done — close the init stopwatch on the same signal.
            _whisperInitSw?.Stop();
        }

        if (_vadSpeechSec < 0)
        {
            var m = s_vadSpeechRegex.Match(msg);
            if (m.Success && float.TryParse(m.Groups[1].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out float sp))
            {
                _vadSpeechSec = sp;
            }
        }
        if (_vadSegments < 0)
        {
            var m = s_vadSegmentsRegex.Match(msg);
            if (m.Success && int.TryParse(m.Groups[1].Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int segs))
            {
                _vadSegments = segs;
            }
        }
        if (_vadReductionPct < 0)
        {
            var m = s_vadReductionRegex.Match(msg);
            if (m.Success && float.TryParse(m.Groups[1].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
            {
                _vadReductionPct = pct;
            }
        }
        if (_vadInferenceMs < 0)
        {
            var m = s_vadInferenceRegex.Match(msg);
            if (m.Success && float.TryParse(m.Groups[1].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out float ms))
            {
                _vadInferenceMs = ms;
            }
        }
        if (_vadMappingPoints < 0)
        {
            var m = s_vadMappingRegex.Match(msg);
            if (m.Success && int.TryParse(m.Groups[1].Value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int pts))
            {
                _vadMappingPoints = pts;
            }
        }
    }

    private void EmitVadSummary(double vadSec)
    {
        var parts = new List<string>();
        if (_vadSegments >= 0) parts.Add($"{_vadSegments} segments");
        if (_vadSpeechSec >= 0) parts.Add($"speech {_vadSpeechSec:F1} s");
        if (_vadReductionPct >= 0) parts.Add($"reduction {_vadReductionPct:F1}%");
        if (_vadInferenceMs >= 0) parts.Add($"inference {_vadInferenceMs:F0} ms");
        if (_vadMappingPoints >= 0) parts.Add($"mapping {_vadMappingPoints} pts");
        parts.Add($"wall {vadSec:F1} s");
        DeckleWhispSource.Log.VadParsed(string.Join(" | ", parts));
    }

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

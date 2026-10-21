using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.Transcription.Whisper;

public sealed partial class WhisperBackend
{
    public Task<TranscriptionResult> TranscribeAsync(
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct,
        TranscriptionContext? context = null)
    {
        return Task.FromResult(TranscribeSync(pcmSamples, segmentSink, ct, context));
    }

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
    // Added to the logged segment t0/t1 so a streaming segment reads its true
    // position in the take; 0 for a standalone call.
    private double _timelineOffsetSec;
    private readonly RepetitionDetector _repetitionDetector = new();
    // Outlives the call on purpose: it describes the native decoder context the
    // next call would inherit.
    private bool _previousCallFilteredText;

    private TranscriptionResult TranscribeSync(
        ReadOnlyMemory<float> pcmSamples,
        Action<TranscriptionSegment>? segmentSink,
        CancellationToken ct,
        TranscriptionContext? context = null)
    {
        IntPtr ctx = _ctx;
        if (ctx == IntPtr.Zero)
        {
            // The same -1 a native failure can return: the reason tells the
            // two apart in the log.
            DeckleWhisperSource.Log.TranscribeFailed("no_context", -1);
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
        _segmentCallbackFailures = 0;
        _repetitionDetector.Reset();

        IntPtr fullParamsPtr = WhisperPInvoke.whisper_full_default_params_by_ref(0);
        WhisperFullParams wparams = Marshal.PtrToStructure<WhisperFullParams>(fullParamsPtr);
        WhisperPInvoke.whisper_free_params(fullParamsPtr);

        // Native stdout-style output can contain recognized text. Keep every
        // print channel closed explicitly; Deckle emits its own content-free
        // operational measurements and routes text only through consented
        // dataset events.
        wparams.print_special = 0;
        wparams.print_progress = 0;
        wparams.print_realtime = 0;
        wparams.print_timestamps = 0;

        // A segment the filter left out of the previous call is still in the
        // native decoder context; continuing from it would prime this call with
        // the very credit line Deckle refused to deliver.
        bool continuesPreviousCall =
            (context?.ContinuesPreviousCall ?? false) && !_previousCallFilteredText;

        var TranscriptionSettings = _host.Transcription;
        WhisperParamsMapper.NativeAllocations nativeAllocs =
            WhisperParamsMapper.Apply(
                ref wparams, TranscriptionSettings, _host.ResolveModelsDirectory(),
                context?.PrimingText, continuesPreviousCall);

        _tokenBeg = WhisperPInvoke.whisper_token_beg(ctx);

        _segmentCallback = OnNewSegment;
        wparams.new_segment_callback = Marshal.GetFunctionPointerForDelegate(_segmentCallback);
        wparams.new_segment_callback_user_data = IntPtr.Zero;

        _abortCallback = _ => _abortRequested || ct.IsCancellationRequested;
        wparams.abort_callback = Marshal.GetFunctionPointerForDelegate(_abortCallback);
        wparams.abort_callback_user_data = IntPtr.Zero;

        _encoderBeginCallback = OnEncoderBegin;
        wparams.encoder_begin_callback = Marshal.GetFunctionPointerForDelegate(_encoderBeginCallback);
        wparams.encoder_begin_callback_user_data = IntPtr.Zero;

        _timelineOffsetSec = context?.TimelineOffsetSec ?? 0;

        // A standalone call and the first utterance of a streaming take log the
        // configuration; the rest skip it, since it is identical across the take.
        if (context?.EmitPreamble ?? true)
        {
            DeckleWhispSource.Log.TranscribeStarted();
            if (OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Transcription,
                    DeckleWhispSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Pipeline))
            {
                string strategyLabel = wparams.strategy == 1
                    ? $"beam{wparams.beam_search_beam_size}"
                    : "greedy";
                float audioSec = (float)pcmSamples.Length / 16_000f;
                DeckleWhispSource.Log.TranscribeStartDetail(
                    audioSec, pcmSamples.Length, strategyLabel);

                string strategyVerbose = wparams.strategy == 1
                    ? $"beam(size={wparams.beam_search_beam_size})"
                    : "greedy";
                DeckleWhispSource.Log.TranscribeParams(
                    $"strategy={strategyVerbose} | temp={wparams.temperature:F2}+{wparams.temperature_inc:F2}" +
                    $" | logprob_thold={wparams.logprob_thold:F2} | entropy_thold={wparams.entropy_thold:F2}" +
                    $" | no_speech_thold={wparams.no_speech_thold:F2} | suppress_nst={wparams.suppress_nst}" +
                    $" | carry_prompt={wparams.carry_initial_prompt} | no_context={wparams.no_context}" +
                    $" | n_threads={wparams.n_threads}");

                string prompt = context?.PrimingText ?? TranscriptionSettings.Engine.InitialPrompt;
                bool carry = TranscriptionSettings.Engine.CarryInitialPrompt;
                if (!string.IsNullOrEmpty(prompt))
                {
                    DeckleWhispSource.Log.TranscribePromptConfigured(prompt.Length, carry);
                }
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
            DeckleWhisperSource.Log.TranscribeFailed("native_error", result);
            return new TranscriptionResult(
                Array.Empty<TranscriptionSegment>(), "", totalMs, whisperInitMs, aborted, result);
        }

        TranscriptionSegment[] segments;
        lock (_segmentsLock)
        {
            segments = _segmentsLocal.ToArray();
        }

        if (_segmentCallbackIncidentOpen && _segmentCallbackFailures == 0 && segments.Length > 0)
        {
            _segmentCallbackIncidentOpen = false;
            DeckleWhisperSource.Log.SegmentCallbackRecovered();
        }
        // FullText is the only text every pipeline delivers, so the filter sits
        // here once. A hit leaves its segment out of FullText; the segments stay
        // whole for telemetry, and an empty FullText already reads as "no text"
        // to both the streaming consumer and the monolithic finalize.
        string fullText = KnownHallucinations.StripFrom(
            segments, out IReadOnlyList<FilteredHallucination> filtered);
        _previousCallFilteredText = filtered.Count > 0;
        if (filtered.Count > 0)
        {
            DeckleWhispSource.Log.TranscribeHallucinationFiltered();
            foreach (FilteredHallucination hit in filtered)
            {
                DeckleWhispSource.Log.TranscribeHallucinationFilteredMetrics(
                    hit.SegmentIndex + 1, segments.Length, hit.Characters, hit.Entry);
            }
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
                bool detailEnabled = OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Transcription,
                    DeckleWhispSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Pipeline);
                float sumP = 0f, minP = 1f;
                int textTok = 0;
                for (int k = 0; k < nTok; k++)
                {
                    int id = WhisperPInvoke.whisper_full_get_token_id(ctx, i, k);
                    if (id >= tokenBeg) continue;
                    float p = WhisperPInvoke.whisper_full_get_token_p(ctx, i, k);
                    sumP += p;
                    if (detailEnabled && p < minP) minP = p;
                    textTok++;
                }
                float avgP = textTok > 0 ? sumP / textTok : 0f;
                if (textTok == 0) minP = 0f;

                var segment = new TranscriptionSegment(segText, t0, t1, avgP, nsp);
                lock (_segmentsLock) _segmentsLocal.Add(segment);

                // abort_callback is probed between decoder steps, so one more
                // segment may surface after the guard trips.
                if (!_abortRequested &&
                    _repetitionDetector.ObserveAndShouldAbort(segText, out int streak, out int period))
                {
                    _abortRequested = true;
                    DeckleWhispSource.Log.TranscribeRepetitionLoop();
                    DeckleWhispSource.Log.TranscribeRepetitionLoopMetrics(streak, period);
                }

                _segmentSink?.Invoke(segment);

                if (detailEnabled)
                {
                    double t0Sec = _timelineOffsetSec + t0 / 100.0;
                    double t1Sec = _timelineOffsetSec + t1 / 100.0;
                    double dur = (t1 - t0) / 100.0;
                    int characters = segText.Trim().Length;
                    DeckleWhispSource.Log.SegmentRecognized(
                        i + 1,
                        t0Sec,
                        t1Sec,
                        dur,
                        nsp,
                        avgP,
                        minP,
                        textTok,
                        nTok,
                        characters);
                }
            }
        }
        catch (Exception ex)
        {
            // A sink that throws once usually throws at every take: one Error
            // per incident, the raw failures in the gated detail.
            int failures = ++_segmentCallbackFailures;
            if (!_segmentCallbackIncidentOpen)
            {
                _segmentCallbackIncidentOpen = true;
                DeckleWhisperSource.Log.SegmentCallbackFailed(ex.GetType().Name, ex.Message);
            }
            DeckleWhisperSource.Log.SegmentCallbackFailedDetail(failures, ex.GetType().Name, ex.Message);
        }
    }

    // whisper.cpp encoder_begin_callback signature:
    //   bool fn(whisper_context* ctx, whisper_state* state, void* user_data)
    // Returning false aborts whisper_full, so this one always returns true.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool WhisperEncoderBeginCallback(IntPtr ctx, IntPtr state, IntPtr user_data);

    private WhisperEncoderBeginCallback? _encoderBeginCallback;

    // The first encoder pass ends whisper.cpp's pre-decode setup (mel
    // spectrogram, VAD when enabled, state reset); later passes, one per 30 s
    // window, must not move the mark.
    private bool OnEncoderBegin(IntPtr ctx, IntPtr state, IntPtr user_data)
    {
        if (_whisperInitSw is { IsRunning: true }) _whisperInitSw.Stop();
        return true;
    }

    // Times whisper.cpp's pre-decode setup: started just before whisper_full,
    // stopped by the first encoder_begin_callback. whisper_init_ms is that
    // setup and whisper_ms (total minus init) the encode and decode. A call
    // that never reaches the encoder stops it when whisper_full returns.
    private Stopwatch? _whisperInitSw;

    // Per call: reset at the top of TranscribeSync.
    private int _segmentCallbackFailures;

    // Across calls: closed by the first call whose segments all land.
    private bool _segmentCallbackIncidentOpen;
}

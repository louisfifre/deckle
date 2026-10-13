using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Threading;
using Deckle.Audio;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Audio;

// Microphone capture engine — owns the waveIn handle, the polling loop,
// the per-recording RMS log, and the post-recording telemetry payload.
//
// Capture has no notion of pipeline state — it just blocks in Record() until
// the cancellation token fires or the duration cap hits, then returns the
// captured float[] and a structured outcome.
//
// Probe() and Record() are NOT re-entrant on a single instance — the
// orchestrator's state machine (Idle → Starting → Recording → Stopping)
// serializes them.
//
// All public events fire from the recording thread (the thread that
// called Record()). Subscribers are responsible for marshaling to the
// UI thread.
public sealed class MicrophoneCapture : IMicrophoneCapture
{
    // 50 ms buffers (1600 bytes) so AudioLevel events fire at a steady ~20 Hz:
    // larger buffers arrive in bursts, and the HUD outline cannot react inside a
    // spoken word. 4 circular buffers give 200 ms of headroom if the drain loop
    // stalls.
    private const uint WAVE_MAPPER    = 0xFFFFFFFF;
    private const uint CALLBACK_EVENT = 0x00050000;
    private const int  N_BUFFERS      = 4;
    private const int  BYTES_PER_BUF  = 16000 * 2 * 50 / 1000; // 50ms × 16kHz × 2 bytes/sample

    // Per-recording RMS history — one linear-RMS sample per 50 ms sub-window,
    // appended by WaveInLoop.EmitSubWindows. Cleared at every Record() entry.
    // Drives the tail diagnostic at Stop and the per-recording mic telemetry
    // summary that auto-calibration reads.
    private readonly List<float> _rmsLog = new(capacity: 20 * 60 * 10);

    // Microphone level, linear RMS [0, 1], one emission per 50 ms sub-window
    // (~20 Hz).
    public event System.Action<float>? AudioLevel;

    // Per-sub-window capture frame (50 ms PCM float[-1, 1] + its linear RMS).
    // Opt-in: when nothing is subscribed at Record() entry, the loop skips the
    // float conversion for the whole take and only AudioLevel fires, so a
    // consumer must subscribe before calling Record().
    public event System.Action<CaptureFrame>? Frame;

    // Fired after waveInStart succeeds — the mic is now live and the first
    // 50 ms buffer is on its way. The orchestrator closes its hotkey-to-capture
    // latency stopwatch on it.
    public event System.Action? CaptureStarted;

    // Fired once during a Record() call when the live low-audio tracker
    // crossed its threshold (no sustained healthy voice in the first 5 s).
    // The orchestrator surfaces the localized warning — Capture itself stays
    // free of any Loc.Get dependency.
    public event System.Action? LowAudioDetected;

    public MicrophoneCapture()
    {
    }

    // ── Pre-flight probe ──────────────────────────────────────────────────────
    //
    // Attempts waveInOpen + waveInClose in sequence with the target format and
    // configured device. If it passes, we know the recording session can start;
    // otherwise the MMSYSERR code maps to a MicErrorKind for the orchestrator
    // to localize.
    public ProbeResult Probe(int deviceId)
    {
        var wfx = new WAVEFORMATEX
        {
            wFormatTag      = 1,
            nChannels       = 1,
            nSamplesPerSec  = 16000,
            nAvgBytesPerSec = 32000,
            nBlockAlign     = 2,
            wBitsPerSample  = 16,
            cbSize          = 0,
        };

        uint deviceIdRaw = deviceId < 0 ? WAVE_MAPPER : (uint)deviceId;

        uint err = NativeMethods.waveInOpen(
            out System.IntPtr hWaveIn,
            deviceIdRaw,
            ref wfx,
            System.IntPtr.Zero,
            System.IntPtr.Zero,
            0u);
        if (err != 0)
        {
            return new ProbeResult(false, MapMmsysErr(err), err);
        }

        NativeMethods.waveInClose(hWaveIn);
        return new ProbeResult(true, MicErrorKind.None, 0);
    }

    private static MicErrorKind MapMmsysErr(uint err) => err switch
    {
        0 => MicErrorKind.None,
        2 => MicErrorKind.NotDetected, // MMSYSERR_BADDEVICEID
        6 => MicErrorKind.NotDetected, // MMSYSERR_NODRIVER
        4 => MicErrorKind.InUse,       // MMSYSERR_ALLOCATED
        _ => MicErrorKind.Unavailable,
    };

    // ── Audio recording ──────────────────────────────────────────────────────
    //
    // Captures the microphone continuously into a single resizable buffer.
    // When `ct` fires (set by RequestToggle on Recording → Stopping CAS in
    // the orchestrator) or the cap-duration branch hits internally, returns
    // all accumulated audio as float[] (PCM16 → float [-1, 1]). No chunking
    // here: a consumer that needs utterances cuts them from the Frame stream.
    public CaptureResult Record(IAudioRecordingHost host, CancellationToken ct)
    {
        System.IntPtr hEvent = NativeMethods.CreateEvent(
            System.IntPtr.Zero, bManualReset: false, bInitialState: false, null);

        // 16 kHz mono PCM16 is fixed, not a setting: Whisper expects it, and
        // WaveInLoop, TranscriptionPreprocessor and AudioFileDecoder assume it.
        var wfx = new WAVEFORMATEX
        {
            wFormatTag      = 1,     // uncompressed PCM
            nChannels       = 1,     // mono
            nSamplesPerSec  = 16000,
            nAvgBytesPerSec = 32000,
            nBlockAlign     = 2,
            wBitsPerSample  = 16,
            cbSize          = 0,
        };

        int configuredDevice = host.AudioInputDeviceId;
        uint deviceId = configuredDevice < 0 ? WAVE_MAPPER : (uint)configuredDevice;

        uint err = NativeMethods.waveInOpen(
            out System.IntPtr hWaveIn, deviceId, ref wfx, hEvent,
            System.IntPtr.Zero, CALLBACK_EVENT);
        if (err != 0)
        {
            DeckleAudioSource.Log.MicrophoneOpenFailed();
            DeckleAudioSource.Log.MicrophoneOpenFailedDetail(err);
            NativeMethods.CloseHandle(hEvent);
            return new CaptureResult(
                Pcm:            System.Array.Empty<float>(),
                Telemetry:      null,
                Outcome:        CaptureOutcome.MicError,
                DrainDuration:  System.TimeSpan.Zero,
                MmsysErr:       err);
        }

        uint hdrSize = (uint)Marshal.SizeOf<WAVEHDR>();
        var hdrPtrs = new System.IntPtr[N_BUFFERS];
        var bufPtrs = new System.IntPtr[N_BUFFERS];

        // Buffer-prep + start guarded on its own. The device is open from here
        // on; if AllocHGlobal OOMs, StructureToPtr/Prepare/AddBuffer faults on a
        // driver, or waveInStart throws, the main try/finally below has not been
        // entered yet — so we'd leak the open device (keeping the mic 'in use'
        // until process exit) plus every buffer allocated so far. We unwind
        // exactly the state acquired and rethrow. `prepared` counts headers that
        // cleared waveInPrepareHeader, so we never unprepare a header that was
        // only allocated by a partial last iteration; FreeHGlobal(IntPtr.Zero)
        // is a documented no-op, so freeing the whole array stays safe.
        int prepared = 0;
        try
        {
            for (int i = 0; i < N_BUFFERS; i++)
            {
                bufPtrs[i] = Marshal.AllocHGlobal(BYTES_PER_BUF);
                hdrPtrs[i] = Marshal.AllocHGlobal((int)hdrSize);
                Marshal.StructureToPtr(new WAVEHDR
                {
                    lpData         = bufPtrs[i],
                    dwBufferLength = BYTES_PER_BUF,
                }, hdrPtrs[i], fDeleteOld: false);
                NativeMethods.waveInPrepareHeader(hWaveIn, hdrPtrs[i], hdrSize);
                prepared = i + 1;
                NativeMethods.waveInAddBuffer(hWaveIn, hdrPtrs[i], hdrSize);
            }

            NativeMethods.waveInStart(hWaveIn);
        }
        catch
        {
            // Unprepare only headers that cleared waveInPrepareHeader.
            // waveInReset is unnecessary: on this path the device was never
            // successfully started — waveInStart is the last guarded
            // statement, and if it threw no buffer ran.
            for (int i = 0; i < prepared; i++)
            {
                NativeMethods.waveInUnprepareHeader(hWaveIn, hdrPtrs[i], hdrSize);
            }
            for (int i = 0; i < N_BUFFERS; i++)
            {
                Marshal.FreeHGlobal(bufPtrs[i]);
                Marshal.FreeHGlobal(hdrPtrs[i]);
            }
            NativeMethods.waveInClose(hWaveIn);
            NativeMethods.CloseHandle(hEvent);
            throw;
        }
        CaptureStarted?.Invoke();

        // Reset the per-recording RMS series — the previous session's tail
        // must not leak into this run's telemetry summary.
        _rmsLog.Clear();

        PumpResult pump = default;
        try
        {
            pump = WaveInLoop.Pump(
                hWaveIn:             hWaveIn,
                hEvent:              hEvent,
                hdrPtrs:             hdrPtrs,
                nBuffers:            N_BUFFERS,
                hdrSize:             hdrSize,
                host:                host,
                rmsLog:              _rmsLog,
                audioLevelCallback:  rms => AudioLevel?.Invoke(rms),
                // Gate the frame conversion on a subscriber present at entry:
                // null here means EmitSubWindows never materialises a float[].
                frameCallback:       Frame is null ? null : frame => Frame?.Invoke(frame),
                onLowAudioDetected:  () => LowAudioDetected?.Invoke(),
                ct:                  ct);
        }
        finally
        {
            // waveInUnprepareHeader runs inside Pump's drain loop already; we
            // still need to free the unmanaged buffers + the device + event
            // handle.
            for (int i = 0; i < N_BUFFERS; i++)
            {
                Marshal.FreeHGlobal(bufPtrs[i]);
                Marshal.FreeHGlobal(hdrPtrs[i]);
            }
            NativeMethods.waveInClose(hWaveIn);
            NativeMethods.CloseHandle(hEvent);
        }

        byte[] allBytes = pump.Pcm ?? System.Array.Empty<byte>();
        double totalSec = allBytes.Length / 32000.0;

        DeckleAudioSource.Log.RecordingCompleted();
        DeckleAudioSource.Log.RecordingCompletedDetail(totalSec);

        if (OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription,
                DeckleAudioSource.Log,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Capture))
        {
            // Full-buffer aggregate exists only for the operational detail.
            // Refuse it before the O(n) PCM pass when details are disabled.
            double aggSumSq = 0;
            double aggPeak  = 0;
            int nAggSamples = allBytes.Length / 2;
            for (int i = 0; i < nAggSamples; i++)
            {
                short s = (short)(allBytes[i * 2] | (allBytes[i * 2 + 1] << 8));
                double v = s / 32768.0;
                aggSumSq += v * v;
                double av = v < 0 ? -v : v;
                if (av > aggPeak) aggPeak = av;
            }
            double rmsAvg  = nAggSamples > 0 ? System.Math.Sqrt(aggSumSq / nAggSamples) : 0;
            double dbfsAvg = rmsAvg > 0 ? 20.0 * System.Math.Log10(rmsAvg) : -120.0;

            DeckleAudioSource.Log.CaptureCompleted(
                totalSec, pump.BuffersReceived, allBytes.Length,
                rmsAvg, aggPeak, dbfsAvg);
        }

        MicrophoneTelemetryPayload? telemetry = null;
        var tail = MicrophoneTelemetryCalculator.ComputeTail(_rmsLog);
        if (tail is null)
        {
            DeckleAudioSource.Log.MicrophoneTelemetryEmpty();
        }
        else
        {
            if (OperationalLogAdmission.IsScopedDetailEnabled(
                    OperationalLogActivity.Transcription,
                    DeckleAudioSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Capture))
            {
                // The sentence and its payload exist only for admitted detail.
                // The tail measurements themselves remain functional input to
                // telemetry and auto-calibration below.
                string headline = MicrophoneTelemetryCalculator.DescribeTail(tail.Value);
                DeckleAudioSource.Log.RecordingTailSummary(headline);
                DeckleAudioSource.Log.RecordingTailSummaryDetail(
                    tail.Value.TailMs, tail.Value.TailDbfs);
            }

            telemetry = MicrophoneTelemetryCalculator.Compute(_rmsLog, tail.Value);
        }

        // The payload is always *computed*: the orchestrator's auto-calibration
        // reads it from the result. The host's toggle only decides whether it's
        // broadcast.
        if (telemetry is not null && host.MicrophoneTelemetryEnabled)
        {
            DeckleAudioSource.Log.MicrophoneTelemetryRecorded(
                telemetry.DurationSeconds,
                telemetry.Samples,
                telemetry.MinDbfs,
                telemetry.P10Dbfs,
                telemetry.P25Dbfs,
                telemetry.P50Dbfs,
                telemetry.P75Dbfs,
                telemetry.P90Dbfs,
                telemetry.MaxDbfs,
                telemetry.MeanRms,
                telemetry.MeanDbfs,
                telemetry.TailRms,
                telemetry.TailDbfs,
                telemetry.TailState);
        }

        float[] pcmFloat = PcmConversion.PcmToFloat(allBytes);

        var outcome = pump.Exit switch
        {
            LoopExit.CapHit => CaptureOutcome.CapHit,
            _               => CaptureOutcome.Completed,
        };

        return new CaptureResult(
            Pcm:           pcmFloat,
            Telemetry:     telemetry,
            Outcome:       outcome,
            DrainDuration: pump.DrainDuration,
            MmsysErr:      0);
    }

    public void Dispose()
    {
        // No persistent native resource — all waveIn handles are scoped to a
        // single Record() call and freed in its `finally`.
    }
}

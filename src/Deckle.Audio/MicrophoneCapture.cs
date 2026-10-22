using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using System.Threading;
using Deckle.Audio;
using Deckle.Audio.Internal;
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
            return new ProbeResult(false, ClassifyError(err), err);
        }

        NativeMethods.waveInClose(hWaveIn);
        return new ProbeResult(true, MicErrorKind.None, 0);
    }

    // The kind a waveIn error code stands for. Public because Record() returns
    // the raw code (CaptureResult.MmsysErr) and a caller that reacts to a failed
    // capture, not only to a failed probe, needs the same reading of it.
    public static MicErrorKind ClassifyError(uint err) => err switch
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
        string purpose = host.RecordingPurpose;
        int configuredDevice = host.AudioInputDeviceId;
        var startClock = System.Diagnostics.Stopwatch.StartNew();

        // The module-local declaration keeps the last error. A failed creation
        // used to reach waveInOpen with a null event handle; the loop's wait on
        // that handle would then return at once on every iteration, unlogged.
        System.IntPtr hEvent = WaveDeviceInterop.CreateEvent(
            System.IntPtr.Zero, bManualReset: false, bInitialState: false, null);
        if (hEvent == System.IntPtr.Zero)
        {
            int win32Error = Marshal.GetLastWin32Error();
            WaveDeviceIdentity requested = ResolveDevice(System.IntPtr.Zero, configuredDevice);
            DeckleAudioSource.Log.MicrophoneStartFailed(
                purpose, "create_event", 0, "none", win32Error, requested.DeviceId, requested.Name);
            // MMSYSERR_ERROR so the orchestrator maps it to "unavailable", as
            // any other device failure it cannot name.
            return MicError(1);
        }

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

        uint deviceId = configuredDevice < 0 ? WAVE_MAPPER : (uint)configuredDevice;

        uint err = NativeMethods.waveInOpen(
            out System.IntPtr hWaveIn, deviceId, ref wfx, hEvent,
            System.IntPtr.Zero, CALLBACK_EVENT);
        if (err != 0)
        {
            WaveDeviceIdentity requested = ResolveDevice(System.IntPtr.Zero, configuredDevice);
            DeckleAudioSource.Log.MicrophoneOpenFailed(
                purpose, err, WaveDeviceInterop.MmsysName(err), requested.DeviceId, requested.Name);
            NativeMethods.CloseHandle(hEvent);
            return MicError(err);
        }

        // Resolved once per take, from the opened handle: the device the
        // driver actually gave, which with the wave mapper is whatever Windows
        // holds as default at this instant, not what Settings shows.
        WaveDeviceIdentity device = ResolveDevice(hWaveIn, configuredDevice);
        DeckleAudioSource.Log.CaptureStarted(
            configuredDevice, N_BUFFERS, BYTES_PER_BUF, host.MaxRecordingDurationSeconds);

        uint hdrSize = (uint)Marshal.SizeOf<WAVEHDR>();
        var hdrPtrs = new System.IntPtr[N_BUFFERS];
        var bufPtrs = new System.IntPtr[N_BUFFERS];

        // Buffer-prep + start guarded on its own. The device is open from here
        // on; if AllocHGlobal OOMs, StructureToPtr/Prepare/AddBuffer faults on a
        // driver, or waveInStart throws, the main try/finally below has not been
        // entered yet — so we'd leak the open device (keeping the mic 'in use'
        // until process exit) plus every buffer allocated so far. We unwind
        // exactly the state acquired. `prepared` counts headers that cleared
        // waveInPrepareHeader, so we never unprepare a header that was only
        // allocated by a partial last iteration; FreeHGlobal(IntPtr.Zero) is a
        // documented no-op, so freeing the whole array stays safe.
        //
        // A refused call is a returned code, not a throw: the codes used to be
        // ignored and the take then ran on a device that never delivered. The
        // first refusal stops the start and the take ends as a MicError, the
        // outcome the pipelines already surface for a refused open.
        int prepared = 0;
        string? startStage = null;
        uint startErr = 0;
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
                uint prepareErr = NativeMethods.waveInPrepareHeader(hWaveIn, hdrPtrs[i], hdrSize);
                if (prepareErr != 0)
                {
                    startStage = "prepare_header";
                    startErr = prepareErr;
                    break;
                }
                prepared = i + 1;
                uint addErr = NativeMethods.waveInAddBuffer(hWaveIn, hdrPtrs[i], hdrSize);
                if (addErr != 0)
                {
                    startStage = "add_buffer";
                    startErr = addErr;
                    break;
                }
            }

            if (startStage is null)
            {
                uint runErr = NativeMethods.waveInStart(hWaveIn);
                if (runErr != 0)
                {
                    startStage = "start";
                    startErr = runErr;
                }
            }
        }
        catch (System.Exception ex)
        {
            // No RecordingStarted was emitted, so this is the take's only
            // trace; the caller still receives the exception.
            DeckleAudioSource.Log.RecordingFailed(
                purpose, ex.GetType().Name, ex.Message, startClock.ElapsedMilliseconds);
            ReleaseAfterFailedStart(hWaveIn, hEvent, hdrPtrs, bufPtrs, prepared, hdrSize);
            throw;
        }

        if (startStage is not null)
        {
            DeckleAudioSource.Log.MicrophoneStartFailed(
                purpose, startStage, startErr, WaveDeviceInterop.MmsysName(startErr), 0,
                device.DeviceId, device.Name);
            ReleaseAfterFailedStart(hWaveIn, hEvent, hdrPtrs, bufPtrs, prepared, hdrSize);
            return MicError(startErr);
        }

        DeckleAudioSource.Log.RecordingStarted(
            purpose, device.Name, device.DeviceId, device.IsDefault,
            (int)wfx.nSamplesPerSec, wfx.nChannels, wfx.wBitsPerSample);

        // From here every exit releases the device in the finally and closes
        // the take with exactly one end: RecordingFailed on a throw (rethrown),
        // RecordingCompleted after the release otherwise. The CaptureStarted
        // subscribers run inside the try so a throwing one no longer leaks the
        // open device.
        var takeClock = System.Diagnostics.Stopwatch.StartNew();
        PumpResult pump = default;
        try
        {
            CaptureStarted?.Invoke();

            // Reset the per-recording RMS series — the previous session's tail
            // must not leak into this run's telemetry summary.
            _rmsLog.Clear();

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
                device:              device,
                ct:                  ct);
        }
        catch (System.Exception ex)
        {
            DeckleAudioSource.Log.RecordingFailed(
                purpose, ex.GetType().Name, ex.Message, takeClock.ElapsedMilliseconds);
            throw;
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
            uint closeErr = NativeMethods.waveInClose(hWaveIn);
            NativeMethods.CloseHandle(hEvent);

            // After a throw pump is default and the drain never ran, so a
            // refused close is then the expected trace of buffers still queued.
            if (pump.ReleaseStage is not null)
                ObserveRelease(pump.ReleaseStage, pump.ReleaseErr);
            else
                ObserveRelease(closeErr != 0 ? "close" : null, closeErr);
        }

        byte[] allBytes = pump.Pcm ?? System.Array.Empty<byte>();
        // The pump's own total, not allBytes: a host that retains no audio
        // returns an empty buffer for a take that still had a length.
        double totalSec = pump.TotalBytes / 32000.0;

        DeckleAudioSource.Log.RecordingCompleted(
            purpose, pump.Exit == LoopExit.CapHit ? "cap_hit" : "stop_requested", totalSec);

        if (host.RetainsAudio && OperationalLogAdmission.IsScopedDetailEnabled(
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

    private static CaptureResult MicError(uint err) => new(
        Pcm:           System.Array.Empty<float>(),
        Telemetry:     null,
        Outcome:       CaptureOutcome.MicError,
        DrainDuration: System.TimeSpan.Zero,
        MmsysErr:      err);

    // The name costs driver calls only the log reads, so it is skipped when no
    // listener is attached.
    private static WaveDeviceIdentity ResolveDevice(System.IntPtr hWaveIn, int configuredDevice)
        => DeckleAudioSource.Log.IsEnabled()
            ? WaveDeviceInterop.ResolveCapture(hWaveIn, configuredDevice)
            : WaveDeviceIdentity.Unresolved(configuredDevice);

    // Unwinds a device that opened but did not start. waveInReset first: the
    // buffers already handed to waveInAddBuffer stay queued until it returns
    // them, and unpreparing a queued header is refused (WAVERR_STILLPLAYING).
    private static void ReleaseAfterFailedStart(
        System.IntPtr hWaveIn, System.IntPtr hEvent,
        System.IntPtr[] hdrPtrs, System.IntPtr[] bufPtrs, int prepared, uint hdrSize)
    {
        string? stage = null;
        uint stageErr = 0;
        void Note(string name, uint code)
        {
            if (code != 0 && stage is null)
            {
                stage = name;
                stageErr = code;
            }
        }

        Note("reset", WaveDeviceInterop.waveInReset(hWaveIn));
        // Unprepare only headers that cleared waveInPrepareHeader.
        for (int i = 0; i < prepared; i++)
        {
            Note("unprepare_header", NativeMethods.waveInUnprepareHeader(hWaveIn, hdrPtrs[i], hdrSize));
        }
        for (int i = 0; i < hdrPtrs.Length; i++)
        {
            Marshal.FreeHGlobal(bufPtrs[i]);
            Marshal.FreeHGlobal(hdrPtrs[i]);
        }
        Note("close", NativeMethods.waveInClose(hWaveIn));
        NativeMethods.CloseHandle(hEvent);
        ObserveRelease(stage, stageErr);
    }

    // Release incident across takes, process-wide: a held device outlives the
    // MicrophoneCapture instance that failed to close it (the Settings testers
    // use a fresh one per take), so the state is static. Locked because the
    // Settings testers and a dictation can each run a take on their own thread.
    private static readonly object s_releaseLock = new();
    private static int s_releaseFailures;

    private static void ObserveRelease(string? stage, uint err)
    {
        lock (s_releaseLock)
        {
            if (stage is null)
            {
                if (s_releaseFailures == 0) return;
                DeckleAudioSource.Log.CaptureReleaseRecovered(s_releaseFailures);
                s_releaseFailures = 0;
                return;
            }

            s_releaseFailures++;
            if (s_releaseFailures == 1)
                DeckleAudioSource.Log.CaptureReleaseFailed(stage, err, WaveDeviceInterop.MmsysName(err));
            DeckleAudioSource.Log.CaptureReleaseDetail(stage, err, s_releaseFailures);
        }
    }

    public void Dispose()
    {
        // No persistent native resource — all waveIn handles are scoped to a
        // single Record() call and freed in its `finally`.
    }
}

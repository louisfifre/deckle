using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using Deckle.Audio;
using Deckle.Audio.Internal;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Audio;

// Microphone capture engine: opens the microphone through PortAudio (its
// WASAPI host API on Windows), runs the capture loop, keeps the per-recording
// RMS log and builds the post-recording telemetry payload.
//
// The signal. The microphone is opened in the raw processing mode when the
// device gives it (no effects, no gain control, no noise suppression between
// the device and Deckle), and in the default mode otherwise, which is logged
// as a fallback; exclusive when the setting asks for it, shared otherwise. The
// device delivers its own format; the conversion to 16 kHz mono float is
// Deckle's own code (CapturePipeline), so the processing is identical on every
// machine.
//
// Capture has no notion of pipeline state: it just blocks in Record() until the
// cancellation token fires or the duration cap hits, then returns the captured
// float[] and a structured outcome.
//
// Probe() and Record() are NOT re-entrant on a single instance: the
// orchestrator's state machine (Idle -> Starting -> Recording -> Stopping)
// serializes them.
//
// All public events fire from the recording thread (the thread that called
// Record()). Subscribers are responsible for marshaling to the UI thread.
public sealed class MicrophoneCapture : IMicrophoneCapture
{
    private readonly ICaptureEndpointProvider _provider;

    // Per-recording RMS history: one linear-RMS sample per 50 ms sub-window,
    // appended by CapturePipeline. Cleared at every Record() entry. Drives the
    // tail diagnostic at Stop and the per-recording mic telemetry summary that
    // auto-calibration reads.
    private readonly List<float> _rmsLog = new(capacity: 20 * 60 * 10);

    // Microphone level, linear RMS [0, 1], one emission per 50 ms sub-window
    // (~20 Hz).
    public event System.Action<float>? AudioLevel;

    // Per-sub-window capture frame (50 ms of 16 kHz mono float[-1, 1] + its
    // linear RMS). Opt-in: when nothing is subscribed at Record() entry, no
    // frame array is allocated for the whole take, so a consumer must subscribe
    // before calling Record().
    public event System.Action<CaptureFrame>? Frame;

    // The level of each device packet (10 ms on most microphones), for an
    // instrument that follows the voice as it happens. Opt-in like Frame.
    public event System.Action<CaptureLevel>? Level;

    // Fired once the microphone is open and started, with how it was opened
    // (device, processing mode, format, period).
    public event System.Action<CaptureSource>? Opened;

    // Fired after the stream starts: the mic is now live and the first packet is
    // on its way. The orchestrator closes its hotkey-to-capture latency
    // stopwatch on it.
    public event System.Action? CaptureStarted;

    // Fired once during a Record() call when the live low-audio tracker crossed
    // its threshold (no sustained healthy voice in the first 5 s). The
    // orchestrator surfaces the localized warning; Capture itself stays free of
    // any Loc.Get dependency.
    public event System.Action? LowAudioDetected;

    public MicrophoneCapture() : this(new PortAudioEndpointProvider())
    {
    }

    // The endpoint provider is the seam a test stands a scripted device behind.
    // The sleep is the capture loop's poll wait, replaced by a test that drives
    // the loop without waiting.
    internal MicrophoneCapture(ICaptureEndpointProvider provider, Action<int>? sleep = null)
    {
        _provider = provider;
        _sleep = sleep;
    }

    private readonly Action<int>? _sleep;

    // ── Pre-flight probe ──────────────────────────────────────────────────────
    //
    // Opens and closes the microphone with the configured endpoint, shared,
    // as a dictation would first try it. If it passes, we know the recording
    // session can start; otherwise the code maps to a MicErrorKind for the
    // orchestrator to localize. An empty id is the system default device.
    public ProbeResult Probe(string endpointId)
    {
        CaptureOpenResult open = _provider.Open(endpointId ?? "", new CaptureOptions(Exclusive: false));
        if (open.Endpoint is null)
        {
            uint code = unchecked((uint)open.ErrorCode);
            return new ProbeResult(false, ClassifyError(code), code);
        }

        open.Endpoint.Dispose();
        return new ProbeResult(true, MicErrorKind.None, 0);
    }

    // The kind an error code stands for. Public because Record() returns the
    // raw code (CaptureResult.ErrorCode) and a caller that reacts to a failed
    // capture, not only to a failed probe, needs the same reading of it.
    public static MicErrorKind ClassifyError(uint code) => CaptureErrors.Classify(code);

    // An error code as a person quotes it and its source documents it: an
    // HRESULT in hex as Microsoft writes it (0x88890004), a PortAudio error in
    // decimal as its header lists it (-9996).
    public static string FormatErrorCode(uint code) => CaptureErrors.Format(code);

    // ── Audio recording ──────────────────────────────────────────────────────
    //
    // Captures the microphone continuously into a single resizable buffer.
    // When `ct` fires (set by RequestToggle on Recording -> Stopping CAS in the
    // orchestrator) or the cap-duration branch hits internally, returns all
    // accumulated audio as float[] mono 16 kHz. No chunking here: a consumer
    // that needs utterances cuts them from the Frame stream.
    public CaptureResult Record(IAudioRecordingHost host, CancellationToken ct)
    {
        string purpose = host.RecordingPurpose;
        string configuredEndpoint = host.AudioInputEndpointId ?? "";
        // Pairs RecordingStarted, RecordingCompleted or RecordingFailed and the
        // anomaly summary of this recording in the log.
        string workId = WorkId.Next();
        var startClock = System.Diagnostics.Stopwatch.StartNew();

        CaptureOpenResult open;
        try
        {
            open = _provider.Open(configuredEndpoint, new CaptureOptions(host.ExclusiveMode));
        }
        catch (System.Exception ex)
        {
            // No RecordingStarted was emitted, so this is the take's only
            // trace; the caller still receives the exception.
            DeckleAudioSource.Log.RecordingFailed(
                purpose, ex.GetType().Name, ex.Message, startClock.ElapsedMilliseconds, workId);
            throw;
        }

        CaptureDeviceIdentity device = open.Device;
        foreach (CaptureModeFallback fallback in open.Fallbacks)
        {
            DeckleAudioSource.Log.CaptureModeFallback(
                purpose, fallback.Reason, CaptureErrors.Format(fallback.ErrorCode), CaptureErrors.Name(fallback.ErrorCode),
                device.EndpointId, device.Name);
        }

        if (open.Endpoint is null)
        {
            DeckleAudioSource.Log.MicrophoneOpenFailed(
                purpose, open.FailedStage ?? "open", CaptureErrors.Format(open.ErrorCode), CaptureErrors.Name(open.ErrorCode),
                device.EndpointId, device.Name);
            return MicError(unchecked((uint)open.ErrorCode));
        }

        // From here the endpoint is released by the finally of exactly one of
        // the two blocks below.
        ICaptureEndpoint endpoint = open.Endpoint;
        CaptureEndpointInfo info = endpoint.Info;
        DeckleAudioSource.Log.CaptureStarted(
            configuredEndpoint, info.InputLatencyMs, info.Exclusive, host.MaxRecordingDurationSeconds);

        // A refused start is a returned code, not a throw, and the take then ends
        // as a MicError, the outcome the pipelines already surface for a refused
        // open.
        int startCode;
        try
        {
            startCode = endpoint.Start();
        }
        catch (System.Exception ex)
        {
            DeckleAudioSource.Log.RecordingFailed(
                purpose, ex.GetType().Name, ex.Message, startClock.ElapsedMilliseconds, workId);
            endpoint.Dispose();
            throw;
        }

        if (startCode != 0)
        {
            DeckleAudioSource.Log.MicrophoneOpenFailed(
                purpose, "start", CaptureErrors.Format(startCode), CaptureErrors.Name(startCode),
                device.EndpointId, device.Name);
            endpoint.Dispose();
            return MicError(unchecked((uint)startCode));
        }

        DeckleAudioSource.Log.RecordingStarted(
            purpose, device.Name, device.EndpointId, device.IsDefault,
            info.Mode == CaptureMode.Raw ? "raw" : "default", info.Exclusive,
            info.Format.SampleRate, info.Format.Channels, workId);

        // From here every exit releases the device in the finally and closes the
        // take with exactly one end: RecordingFailed on a throw (rethrown),
        // RecordingCompleted after the release otherwise. The Opened and
        // CaptureStarted subscribers run inside the try so a throwing one no
        // longer leaks the open device.
        var takeClock = System.Diagnostics.Stopwatch.StartNew();
        PumpResult pump = default;
        bool pumped = false;
        try
        {
            Opened?.Invoke(new CaptureSource(
                device.Name, info.Mode, info.Exclusive, info.Format.SampleRate, info.Format.Channels, info.InputLatencyMs));
            CaptureStarted?.Invoke();

            // Reset the per-recording RMS series: the previous session's tail
            // must not leak into this run's telemetry summary.
            _rmsLog.Clear();

            pump = CaptureLoop.Pump(
                endpoint:            endpoint,
                host:                host,
                rmsLog:              _rmsLog,
                audioLevelCallback:  rms => AudioLevel?.Invoke(rms),
                // Gate the frame array on a subscriber present at entry: null
                // here means no float[] is ever materialised per window.
                frameCallback:       Frame is null ? null : frame => Frame?.Invoke(frame),
                levelCallback:       Level is null ? null : level => Level?.Invoke(level),
                onLowAudioDetected:  () => LowAudioDetected?.Invoke(),
                workId:              workId,
                ct:                  ct,
                sleep:               _sleep);
            pumped = true;
        }
        catch (System.Exception ex)
        {
            DeckleAudioSource.Log.RecordingFailed(
                purpose, ex.GetType().Name, ex.Message, takeClock.ElapsedMilliseconds, workId);
            throw;
        }
        finally
        {
            // The loop has already stopped the stream; this closes it, and stops
            // it first after a throw.
            endpoint.Dispose();

            // After a throw the pump never returned, so there is nothing to say
            // about the release.
            if (pumped)
                ObserveRelease(pump.ReleaseStage, pump.ReleaseCode);
        }

        if (pump.Exit == LoopExit.DeviceLost)
        {
            DeckleAudioSource.Log.MicrophoneDeviceLost(
                purpose, CaptureErrors.Format(pump.LostCode), CaptureErrors.Name(pump.LostCode),
                device.EndpointId, device.Name, takeClock.ElapsedMilliseconds);
        }

        float[] pcm = pump.Pcm;
        // The pump's own total, not pcm: a host that retains no audio returns an
        // empty buffer for a take that still had a length.
        double totalSec = pump.TotalSamples / (double)CapturePipeline.OutputRate;

        DeckleAudioSource.Log.RecordingCompleted(
            purpose, pump.Exit switch
            {
                LoopExit.CapHit     => "cap_hit",
                LoopExit.DeviceLost => "device_lost",
                _                   => "stop_requested",
            }, totalSec, workId);

        if (host.RetainsAudio && OperationalLogAdmission.IsScopedDetailEnabled(
                OperationalLogActivity.Transcription,
                DeckleAudioSource.Log,
                EventLevel.Verbose,
                (EventKeywords)Keywords.Capture))
        {
            // Full-buffer aggregate exists only for the operational detail.
            // Refuse it before the O(n) pass when details are disabled.
            double aggSumSq = 0;
            double aggPeak  = 0;
            for (int i = 0; i < pcm.Length; i++)
            {
                double v = pcm[i];
                aggSumSq += v * v;
                double av = v < 0 ? -v : v;
                if (av > aggPeak) aggPeak = av;
            }
            double rmsAvg  = pcm.Length > 0 ? System.Math.Sqrt(aggSumSq / pcm.Length) : 0;
            double dbfsAvg = rmsAvg > 0 ? 20.0 * System.Math.Log10(rmsAvg) : -120.0;

            DeckleAudioSource.Log.CaptureCompleted(
                totalSec, pump.ReadsReceived, pcm.Length * sizeof(float),
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
                // The line exists only for admitted detail. The tail
                // measurements themselves remain functional input to
                // telemetry and auto-calibration below.
                DeckleAudioSource.Log.RecordingTailSummaryDetail(
                    tail.Value.TailMs, tail.Value.TailDbfs, tail.Value.TailState);
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

        var outcome = pump.Exit switch
        {
            LoopExit.CapHit => CaptureOutcome.CapHit,
            _               => CaptureOutcome.Completed,
        };

        return new CaptureResult(
            Pcm:           pcm,
            Telemetry:     telemetry,
            Outcome:       outcome,
            DrainDuration: pump.DrainDuration,
            ErrorCode:     0);
    }

    private static CaptureResult MicError(uint code) => new(
        Pcm:           System.Array.Empty<float>(),
        Telemetry:     null,
        Outcome:       CaptureOutcome.MicError,
        DrainDuration: System.TimeSpan.Zero,
        ErrorCode:     code);

    // Release incident across takes, process-wide: a held device outlives the
    // MicrophoneCapture instance that failed to stop it (the Settings testers
    // use a fresh one per take), so the state is static. Locked because the
    // Settings testers and a dictation can each run a take on their own thread.
    private static readonly object s_releaseLock = new();
    private static int s_releaseFailures;

    private static void ObserveRelease(string? stage, int code)
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
                DeckleAudioSource.Log.CaptureReleaseFailed(stage, CaptureErrors.Format(code), CaptureErrors.Name(code));
            DeckleAudioSource.Log.CaptureReleaseDetail(stage, CaptureErrors.Format(code), s_releaseFailures);
        }
    }

    public void Dispose()
    {
        // No persistent native resource: the audio client is scoped to a single
        // Record() call and released in its `finally`.
    }
}

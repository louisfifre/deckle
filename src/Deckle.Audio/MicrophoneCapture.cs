using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading;
using Deckle.Audio;
using Deckle.Audio.Internal;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Audio;

// Microphone capture engine: opens the microphone through WASAPI, runs the
// capture loop, keeps the per-recording RMS log and builds the post-recording
// telemetry payload.
//
// The signal. The microphone is opened in shared mode with the raw processing
// option when the endpoint supports it (no effects, no gain control, no noise
// suppression between the device and Deckle), and in the default mode
// otherwise, which is logged as a fallback. The device delivers its own mix
// format; the conversion to 16 kHz mono float is Deckle's own code
// (CapturePipeline), so the processing is identical on every machine.
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

    public MicrophoneCapture() : this(new WasapiEndpointProvider())
    {
    }

    // The endpoint provider is the seam a test stands a scripted device behind.
    internal MicrophoneCapture(ICaptureEndpointProvider provider)
    {
        _provider = provider;
    }

    // ── Pre-flight probe ──────────────────────────────────────────────────────
    //
    // Opens and closes the microphone with the configured endpoint. If it
    // passes, we know the recording session can start; otherwise the HRESULT
    // maps to a MicErrorKind for the orchestrator to localize. An empty id is
    // the system default device.
    public ProbeResult Probe(string endpointId)
    {
        using var apartment = ComApartment.Enter();
        CaptureOpenResult open = _provider.Open(endpointId ?? "");
        if (open.Endpoint is null)
        {
            uint code = unchecked((uint)open.Hresult);
            return new ProbeResult(false, ClassifyError(code), code);
        }

        open.Endpoint.Dispose();
        return new ProbeResult(true, MicErrorKind.None, 0);
    }

    // The kind an HRESULT stands for. Public because Record() returns the raw
    // code (CaptureResult.ErrorCode) and a caller that reacts to a failed
    // capture, not only to a failed probe, needs the same reading of it.
    public static MicErrorKind ClassifyError(uint hresult) => WasapiErrors.Classify(hresult);

    // An error code as a person quotes it and Microsoft documents it: an
    // HRESULT in hex, 0x88890004 rather than 2290679812.
    public static string FormatErrorCode(uint hresult) => WasapiErrors.Hex(hresult);

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

        // Declared first so it is disposed last: the COM objects the endpoint
        // holds are released before the thread leaves its apartment.
        using var apartment = ComApartment.Enter();

        CaptureOpenResult open;
        try
        {
            open = _provider.Open(configuredEndpoint);
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
            uint code = unchecked((uint)fallback.Hresult);
            DeckleAudioSource.Log.CaptureModeFallback(
                purpose, fallback.Reason, code, WasapiErrors.Name(code), device.EndpointId, device.Name);
        }

        if (open.Endpoint is null)
        {
            uint code = unchecked((uint)open.Hresult);
            DeckleAudioSource.Log.MicrophoneOpenFailed(
                purpose, open.FailedStage ?? "open", code, WasapiErrors.Name(code), device.EndpointId, device.Name);
            return MicError(code);
        }

        // From here the endpoint is released by the finally of exactly one of
        // the two blocks below.
        ICaptureEndpoint endpoint = open.Endpoint;
        CaptureEndpointInfo info = endpoint.Info;
        DeckleAudioSource.Log.CaptureStarted(
            configuredEndpoint, info.PeriodMs, info.LowLatency, host.MaxRecordingDurationSeconds);

        // A refused start is a returned code, not a throw, and the take then ends
        // as a MicError, the outcome the pipelines already surface for a refused
        // open.
        int startHresult;
        try
        {
            startHresult = endpoint.Start();
        }
        catch (System.Exception ex)
        {
            DeckleAudioSource.Log.RecordingFailed(
                purpose, ex.GetType().Name, ex.Message, startClock.ElapsedMilliseconds, workId);
            endpoint.Dispose();
            throw;
        }

        if (startHresult != 0)
        {
            uint code = unchecked((uint)startHresult);
            DeckleAudioSource.Log.MicrophoneOpenFailed(
                purpose, "start", code, WasapiErrors.Name(code), device.EndpointId, device.Name);
            endpoint.Dispose();
            return MicError(code);
        }

        DeckleAudioSource.Log.RecordingStarted(
            purpose, device.Name, device.EndpointId, device.IsDefault,
            info.Mode == CaptureMode.Raw ? "raw" : "default",
            info.Format.SampleRate, info.Format.Channels, info.Format.BytesPerSample * 8, workId);

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
                device.Name, info.Mode, info.Format.SampleRate, info.Format.Channels, info.PeriodMs));
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
                ct:                  ct);
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
            // The loop has already stopped the stream; this releases the audio
            // client and the event.
            endpoint.Dispose();

            // After a throw the pump never returned, so there is nothing to say
            // about the release.
            if (pumped)
                ObserveRelease(pump.ReleaseStage, unchecked((uint)pump.ReleaseHresult));
        }

        if (pump.Exit == LoopExit.DeviceLost)
        {
            uint code = unchecked((uint)pump.LostHresult);
            DeckleAudioSource.Log.MicrophoneDeviceLost(
                purpose, code, WasapiErrors.Name(code), device.EndpointId, device.Name,
                takeClock.ElapsedMilliseconds);
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
                totalSec, pump.PacketsReceived, pcm.Length * sizeof(float),
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

    private static CaptureResult MicError(uint hresult) => new(
        Pcm:           System.Array.Empty<float>(),
        Telemetry:     null,
        Outcome:       CaptureOutcome.MicError,
        DrainDuration: System.TimeSpan.Zero,
        ErrorCode:     hresult);

    // Release incident across takes, process-wide: a held device outlives the
    // MicrophoneCapture instance that failed to stop it (the Settings testers
    // use a fresh one per take), so the state is static. Locked because the
    // Settings testers and a dictation can each run a take on their own thread.
    private static readonly object s_releaseLock = new();
    private static int s_releaseFailures;

    private static void ObserveRelease(string? stage, uint hresult)
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
                DeckleAudioSource.Log.CaptureReleaseFailed(stage, hresult, WasapiErrors.Name(hresult));
            DeckleAudioSource.Log.CaptureReleaseDetail(stage, hresult, s_releaseFailures);
        }
    }

    public void Dispose()
    {
        // No persistent native resource: the audio client is scoped to a single
        // Record() call and released in its `finally`.
    }
}

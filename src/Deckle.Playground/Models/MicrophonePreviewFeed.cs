using System;
using System.Threading;
using Deckle.Audio;
using Deckle.Transcription;

namespace Deckle.Playground;

// The live source of the HUD tab's Recording preview: it opens the microphone
// and hands the preview what a dictation hands the live HUD, the level of each
// 50 ms and the pause gauge computed from those levels with the dictation's own
// segmenter (UtteranceProgressMeter). It records nothing: the capture keeps no
// audio (RetainsAudio) and no meter or telemetry event is emitted for it.
//
// Threading. Start and Stop belong to the UI thread. The capture runs on a
// thread of its own, which raises every level, and all the sinks are called
// from it, as the live HUD's are. One lock (_gate) orders them against Stop:
// a sink call holds it, and Stop takes it to close the run, so once Stop
// returns no sink is running and none will run again. That is what lets the
// caller change the preview's state right after Stop with no late level
// landing on the new state.
//
// A run that is stopped does not wait for the device to come back. The next
// run does: it waits for the previous run's thread to finish (bounded by
// DeviceReleaseTimeout) before it opens, so a quick Stop then Start never
// asks waveIn for the device while the previous take still holds it.
//
// Whether waveIn lets this open while a live dictation holds the microphone is
// not known from the code. Both outcomes are handled the same way: an open
// that fails is reported to onFailed and the caller moves to the simulation.
internal sealed class MicrophonePreviewFeed
{
    // Long enough for the capture's own drain after a stop, short enough that a
    // device that never comes back shows as the open failing and not as a
    // preview that waits for ever.
    internal static readonly TimeSpan DeviceReleaseTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<IMicrophoneCapture> _createCapture;
    private readonly Func<int> _deviceId;
    private readonly Func<EnergySegmenterSettings> _segmenterSettings;
    private readonly Action<float> _onLevel;
    private readonly Action<UtteranceProgress> _onProgress;
    private readonly Action<MicrophonePreviewFailure> _onFailed;

    // Guards every Run field below and orders the sinks against Stop.
    private readonly object _gate = new();
    private Run? _current;
    private Thread? _lastThread;

    // createCapture, deviceId and segmenterSettings are read at each Start, as a
    // dictation reads them when it begins. onLevel and onProgress are called
    // from the capture thread; onFailed too, once, and never for a run that was
    // stopped.
    public MicrophonePreviewFeed(
        Func<IMicrophoneCapture> createCapture,
        Func<int> deviceId,
        Func<EnergySegmenterSettings> segmenterSettings,
        Action<float> onLevel,
        Action<UtteranceProgress> onProgress,
        Action<MicrophonePreviewFailure> onFailed)
    {
        _createCapture = createCapture;
        _deviceId = deviceId;
        _segmenterSettings = segmenterSettings;
        _onLevel = onLevel;
        _onProgress = onProgress;
        _onFailed = onFailed;
    }

    // Opens the microphone. When it is already open, the take starts over on the
    // same capture, with a fresh gauge: a preview that restarts (a knob moved)
    // does not close and reopen the device each time.
    public void Start()
    {
        var settings = _segmenterSettings();
        int device = _deviceId();

        lock (_gate)
        {
            if (_current is { } running)
            {
                running.Meter = new UtteranceProgressMeter(settings);
                _onProgress(running.Meter.Progress);
                return;
            }

            var run = new Run(new PreviewHost(device), new UtteranceProgressMeter(settings), _lastThread);
            var thread = new Thread(() => Capture(run))
            {
                IsBackground = true,
                Name = "Deckle.Playground.MicrophonePreview",
            };
            _current = run;
            _lastThread = thread;
            // Where a take stands before its first level, published before the
            // first one, as the engine does.
            _onProgress(run.Meter.Progress);
            thread.Start();
        }
    }

    // Closes the microphone. Final: see the threading note above. The gauge is
    // dropped as the engine drops it when its capture ends.
    public void Stop()
    {
        lock (_gate)
        {
            if (_current is not { } run) return;
            _current = null;
            run.Closed = true;
            _onProgress(default);
            run.Cancellation.Cancel();
        }
    }

    private void Capture(Run run)
    {
        MicrophonePreviewFailure? failure = null;
        try
        {
            run.Predecessor?.Join(DeviceReleaseTimeout);
            if (!run.Cancellation.IsCancellationRequested)
            {
                using var capture = _createCapture();
                // Frame stays unsubscribed: the capture then converts no float
                // buffer, and the level it raises is the RMS the Frame carries.
                capture.AudioLevel += rms => Deliver(run, rms);
                CaptureResult result = capture.Record(run.Host, run.Cancellation.Token);
                failure = FailureOf(result, run.Cancellation.IsCancellationRequested);
            }
        }
        catch (Exception ex)
        {
            failure = new MicrophonePreviewFailure(MicrophoneProblem.Faulted, 0, ex.GetType().Name);
        }

        if (Finish(run) && failure is { } failed)
            _onFailed(failed);
    }

    private void Deliver(Run run, float rms)
    {
        lock (_gate)
        {
            if (run.Closed) return;
            _onLevel(rms);
            _onProgress(run.Meter.Push(rms));
        }
    }

    // Closes a run the capture ended on its own, and reports whether it was
    // still open: a run that was stopped has said all it will say.
    private bool Finish(Run run)
    {
        lock (_gate)
        {
            bool wasOpen = !run.Closed;
            if (wasOpen)
            {
                run.Closed = true;
                _onProgress(default);
            }
            if (ReferenceEquals(_current, run)) _current = null;
            // Closed first, so Stop never cancels a disposed source.
            run.Cancellation.Dispose();
            return wasOpen;
        }
    }

    // The host sets no cap, so the capture returns only when asked to stop or
    // when it cannot go on.
    private static MicrophonePreviewFailure? FailureOf(CaptureResult result, bool stopped)
    {
        if (result.Outcome == CaptureOutcome.MicError)
        {
            var problem = MicrophoneCapture.ClassifyError(result.MmsysErr) switch
            {
                MicErrorKind.NotDetected => MicrophoneProblem.NotDetected,
                MicErrorKind.InUse       => MicrophoneProblem.InUse,
                _                        => MicrophoneProblem.Unavailable,
            };
            return new MicrophonePreviewFailure(problem, result.MmsysErr);
        }
        return stopped ? null : new MicrophonePreviewFailure(MicrophoneProblem.Ended);
    }

    // Guarded fields: Meter (replaced by a restarted take) and Closed.
    private sealed class Run(PreviewHost host, UtteranceProgressMeter meter, Thread? predecessor)
    {
        public PreviewHost Host { get; } = host;
        public Thread? Predecessor { get; } = predecessor;
        public CancellationTokenSource Cancellation { get; } = new();
        public UtteranceProgressMeter Meter { get; set; } = meter;
        public bool Closed { get; set; }
    }

    // What a preview takes from the microphone: the configured device, no audio
    // kept, no cap, no telemetry. The purpose tags the capture's own events, so
    // a preview does not read as a dictation in the log.
    private sealed class PreviewHost(int deviceId) : IAudioRecordingHost
    {
        public int AudioInputDeviceId => deviceId;
        public int MaxRecordingDurationSeconds => 0;
        public bool MicrophoneTelemetryEnabled => false;
        public string RecordingPurpose => "playground";
        public bool RetainsAudio => false;
    }
}

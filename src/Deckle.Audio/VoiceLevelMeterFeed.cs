namespace Deckle.Audio;

// ── VoiceLevelMeterFeed ───────────────────────────────────────────────────────
//
// The microphone behind the voice meter on the Recording page: it opens the
// configured device, hands over the RMS of each 50 ms frame, and keeps nothing.
// The capture keeps no audio (RetainsAudio), emits no telemetry, sets no cap,
// and only AudioLevel is subscribed, so no float buffer is converted: the level
// it raises is the same RMS a Frame would carry.
//
// The discipline of the Playground's MicrophonePreviewFeed, which this module
// cannot reference (Playground sits above Audio):
//
// - A thread per run. Start and Stop belong to the UI thread; the capture runs
//   on a thread of its own and calls the level sink from it.
// - One lock (_gate) orders the sinks against Stop: a sink call holds it, and
//   Stop takes it to close the run, so once Stop returns no sink is running
//   and none will run again for that run.
// - A run that is stopped does not wait for the device. The next run does: it
//   joins the previous run's thread (bounded by DeviceReleaseTimeout) before
//   it opens, so a quick Off then On never asks waveIn for a device the
//   previous take still holds.
//
// No log of its own: MicrophoneCapture writes the take's start, end and any
// open failure on the Deckle-Audio provider, tagged with the "voice_meter"
// purpose, which is the record a person reads.
internal sealed class VoiceLevelMeterFeed
{
    // Long enough for the capture's own drain after a stop, short enough that a
    // device that never comes back shows as the open failing, not as a meter
    // that waits for ever.
    internal static readonly TimeSpan DeviceReleaseTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<IMicrophoneCapture> _createCapture;
    private readonly Action<float> _onLevel;

    // Guards every Run field below and orders the sinks against Stop.
    private readonly object _gate = new();
    private Run? _current;
    private Thread? _lastThread;

    // onLevel is called on the capture thread, under the gate.
    public VoiceLevelMeterFeed(Func<IMicrophoneCapture> createCapture, Action<float> onLevel)
    {
        _createCapture = createCapture;
        _onLevel = onLevel;
    }

    public bool IsRunning
    {
        get { lock (_gate) return _current is not null; }
    }

    // Opens the microphone. A no-op while a run is open. onFailed is called once,
    // on the capture thread, when the run ends without being stopped (the
    // device refused, failed, or the capture returned on its own); never for a
    // run that was stopped.
    public void Start(int deviceId, double voiceLevelDbfs, Action<VoiceLevelMeterFailure> onFailed)
    {
        lock (_gate)
        {
            if (_current is not null) return;

            var run = new Run(new MeterHost(deviceId, voiceLevelDbfs), onFailed, _lastThread);
            var thread = new Thread(() => Capture(run))
            {
                IsBackground = true,
                Name = "Deckle.Audio.VoiceLevelMeter",
            };
            _current = run;
            _lastThread = thread;
            thread.Start();
        }
    }

    // Closes the microphone. Final: see the threading note above.
    public void Stop()
    {
        lock (_gate)
        {
            if (_current is not { } run) return;
            _current = null;
            run.Closed = true;
            run.Cancellation.Cancel();
        }
    }

    private void Capture(Run run)
    {
        VoiceLevelMeterFailure? failure = null;
        try
        {
            run.Predecessor?.Join(DeviceReleaseTimeout);
            if (!run.Cancellation.IsCancellationRequested)
            {
                using var capture = _createCapture();
                capture.AudioLevel += rms => Deliver(run, rms);
                CaptureResult result = capture.Record(run.Host, run.Cancellation.Token);
                failure = FailureOf(result, run.Cancellation.IsCancellationRequested);
            }
        }
        catch (Exception)
        {
            // The capture logs what it can name; the meter only needs to know
            // the microphone is not there.
            failure = VoiceLevelMeterFailure.Unavailable;
        }

        if (Finish(run) && failure is { } failed)
            run.OnFailed(failed);
    }

    private void Deliver(Run run, float rms)
    {
        lock (_gate)
        {
            if (run.Closed) return;
            _onLevel(rms);
        }
    }

    // Closes a run the capture ended on its own, and reports whether it was
    // still open: a run that was stopped has said all it will say.
    private bool Finish(Run run)
    {
        lock (_gate)
        {
            bool wasOpen = !run.Closed;
            run.Closed = true;
            if (ReferenceEquals(_current, run)) _current = null;
            // Closed first, so Stop never cancels a disposed source.
            run.Cancellation.Dispose();
            return wasOpen;
        }
    }

    // The host sets no cap, so the capture returns only when asked to stop or
    // when it cannot go on.
    internal static VoiceLevelMeterFailure? FailureOf(CaptureResult result, bool stopped)
    {
        if (result.Outcome == CaptureOutcome.MicError)
        {
            return MicrophoneCapture.ClassifyError(result.MmsysErr) switch
            {
                MicErrorKind.NotDetected => VoiceLevelMeterFailure.NotDetected,
                MicErrorKind.InUse       => VoiceLevelMeterFailure.InUse,
                _                        => VoiceLevelMeterFailure.Unavailable,
            };
        }
        return stopped ? null : VoiceLevelMeterFailure.Ended;
    }

    // Guarded field: Closed.
    private sealed class Run(MeterHost host, Action<VoiceLevelMeterFailure> onFailed, Thread? predecessor)
    {
        public MeterHost Host { get; } = host;
        public Action<VoiceLevelMeterFailure> OnFailed { get; } = onFailed;
        public Thread? Predecessor { get; } = predecessor;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Closed { get; set; }
    }

    // What the meter takes from the microphone: the configured device, no audio
    // kept, no cap, no telemetry. The voice level is read at start; the
    // low-audio warning that reads it stays off for a take that keeps no audio.
    // The purpose tags the capture's own log lines, so a meter left on does not
    // read as a dictation.
    private sealed class MeterHost(int deviceId, double voiceLevelDbfs) : IAudioRecordingHost
    {
        public int AudioInputDeviceId => deviceId;
        public int MaxRecordingDurationSeconds => 0;
        public bool MicrophoneTelemetryEnabled => false;
        public double VoiceLevelDbfs => voiceLevelDbfs;
        public string RecordingPurpose => "voice_meter";
        public bool RetainsAudio => false;
    }
}

// Why the meter's microphone closed without being asked to.
internal enum VoiceLevelMeterFailure
{
    NotDetected,
    InUse,
    Unavailable,
    Ended,
}

namespace Deckle.Audio;

// ── MicrophoneLevelFeed ───────────────────────────────────────────────────────
//
// A live microphone that hands over the RMS of each 50 ms frame and keeps
// nothing: the base of every "show me my microphone" surface, today the
// Recording page's voice meter. The caller's host says
// what is opened and how the capture's own log lines read (its purpose); the
// feed subscribes AudioLevel, the RMS a Frame would carry, and no Frame, so the
// capture allocates no per-frame copy.
//
// Two optional sinks sit beside the 50 ms level. onPulse receives the level of
// each device packet (about 10 ms), as soon as the device delivers it: it is
// what a meter paints, so the bar moves with the voice instead of a frame late.
// It decides nothing; the 50 ms level stays the one a "voice or not" reading
// uses, the one the segmenter cuts on. onSource receives what the capture
// obtained (device, raw or default mode, rate, period) once the device is open.
//
// Threading.
// - A thread per run. Start and Stop belong to the UI thread; the capture runs
//   on a thread of its own and calls the sinks from it.
// - One lock (_gate) orders every sink against Stop: onLevel, onOpened,
//   onClosed and onFailed all run under it, and Stop takes it to close the run,
//   so once Stop returns no sink is running and none will run again for that
//   run. A sink must therefore stay short and must not call back into the feed;
//   a sink that reaches the UI marshals to it.
// - A run that is stopped does not wait for the device. The next run does: it
//   joins the previous run's thread (bounded by DeviceReleaseTimeout) before it
//   opens, so a quick Stop then Start never asks Windows for a device the
//   previous take still holds.
//
// No log of its own: MicrophoneCapture writes the take's start, end and any
// open failure on the Deckle-Audio provider, tagged with the host's purpose,
// and a caller that changes course on a failure logs that decision.
public sealed class MicrophoneLevelFeed
{
    // Long enough for the capture's own drain after a stop, short enough that a
    // device that never comes back shows as the open failing, not as a surface
    // that waits for ever.
    public static readonly TimeSpan DeviceReleaseTimeout = TimeSpan.FromSeconds(3);

    private readonly Func<IMicrophoneCapture> _createCapture;
    private readonly Func<IAudioRecordingHost> _host;
    private readonly Action<float> _onLevel;
    private readonly Action<MicrophoneLevelFeedFailure> _onFailed;
    private readonly Action? _onOpened;
    private readonly Action? _onClosed;
    private readonly Action<CaptureLevel>? _onPulse;
    private readonly Action<CaptureSource>? _onSource;

    // Guards every Run field below and orders the sinks against Stop.
    private readonly object _gate = new();
    private Run? _current;
    private Thread? _lastThread;

    // createCapture and host are read at each Start, on the caller's thread.
    // Under the gate: onLevel on the capture thread for each frame; onOpened on
    // the caller's thread when a run opens, before its first level; onClosed
    // once when an open run closes, by Stop or on its own, before onFailed;
    // onFailed once, on the capture thread, when a run ends without being
    // stopped, and never for a run that was stopped. onPulse and onSource, on
    // the capture thread, under the gate, never after Stop returned.
    public MicrophoneLevelFeed(
        Func<IMicrophoneCapture> createCapture,
        Func<IAudioRecordingHost> host,
        Action<float> onLevel,
        Action<MicrophoneLevelFeedFailure> onFailed,
        Action? onOpened = null,
        Action? onClosed = null,
        Action<CaptureLevel>? onPulse = null,
        Action<CaptureSource>? onSource = null)
    {
        _createCapture = createCapture;
        _host = host;
        _onLevel = onLevel;
        _onFailed = onFailed;
        _onOpened = onOpened;
        _onClosed = onClosed;
        _onPulse = onPulse;
        _onSource = onSource;
    }

    public bool IsRunning
    {
        get { lock (_gate) return _current is not null; }
    }

    // Opens the microphone. Returns false, and does nothing, while a run is
    // already open.
    public bool Start()
    {
        IAudioRecordingHost host = _host();
        lock (_gate)
        {
            if (_current is not null) return false;

            var run = new Run(host, _lastThread);
            var thread = new Thread(() => Capture(run))
            {
                IsBackground = true,
                Name = "Deckle.Audio.MicrophoneLevelFeed",
            };
            _current = run;
            _lastThread = thread;
            _onOpened?.Invoke();
            thread.Start();
            return true;
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
            _onClosed?.Invoke();
            run.Cancellation.Cancel();
        }
    }

    private void Capture(Run run)
    {
        MicrophoneLevelFeedFailure? failure = null;
        try
        {
            run.Predecessor?.Join(DeviceReleaseTimeout);
            if (!run.Cancellation.IsCancellationRequested)
            {
                using var capture = _createCapture();
                capture.AudioLevel += rms => Deliver(run, rms);
                // Subscribed only when asked: an unsubscribed Level costs the
                // capture nothing per packet.
                if (_onPulse is { } onPulse) capture.Level += level => DeliverUnderGate(run, onPulse, level);
                if (_onSource is { } onSource) capture.Opened += source => DeliverUnderGate(run, onSource, source);
                CaptureResult result = capture.Record(run.Host, run.Cancellation.Token);
                failure = FailureOf(result, run.Cancellation.IsCancellationRequested);
            }
        }
        catch (Exception ex)
        {
            // The type only: the message can quote a device's name.
            failure = new MicrophoneLevelFeedFailure(MicrophoneProblem.Faulted, 0, ex.GetType().Name);
        }

        Finish(run, failure);
    }

    private void Deliver(Run run, float rms)
    {
        lock (_gate)
        {
            if (run.Closed) return;
            _onLevel(rms);
        }
    }

    private void DeliverUnderGate<T>(Run run, Action<T> sink, T value)
    {
        lock (_gate)
        {
            if (run.Closed) return;
            sink(value);
        }
    }

    // Closes a run the capture ended, and reports its failure if it was still
    // open: a run that was stopped has said all it will say. Under the gate, so
    // a Stop that returned has no failure of its run left to come.
    private void Finish(Run run, MicrophoneLevelFeedFailure? failure)
    {
        lock (_gate)
        {
            bool wasOpen = !run.Closed;
            run.Closed = true;
            if (ReferenceEquals(_current, run)) _current = null;
            // Closed first, so Stop never cancels a disposed source.
            run.Cancellation.Dispose();
            if (!wasOpen) return;
            _onClosed?.Invoke();
            if (failure is { } failed) _onFailed(failed);
        }
    }

    // The hosts of this feed set no cap, so the capture returns only when asked
    // to stop or when it cannot go on. A failed open keeps the kind the
    // pre-flight probe gives the same error code.
    internal static MicrophoneLevelFeedFailure? FailureOf(CaptureResult result, bool stopped)
    {
        if (result.Outcome == CaptureOutcome.MicError)
        {
            var problem = MicrophoneCapture.ClassifyError(result.ErrorCode) switch
            {
                MicErrorKind.NotDetected => MicrophoneProblem.NotDetected,
                MicErrorKind.InUse       => MicrophoneProblem.InUse,
                _                        => MicrophoneProblem.Unavailable,
            };
            return new MicrophoneLevelFeedFailure(problem, result.ErrorCode);
        }
        return stopped ? null : new MicrophoneLevelFeedFailure(MicrophoneProblem.Ended);
    }

    // Guarded field: Closed.
    private sealed class Run(IAudioRecordingHost host, Thread? predecessor)
    {
        public IAudioRecordingHost Host { get; } = host;
        public Thread? Predecessor { get; } = predecessor;
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Closed { get; set; }
    }
}

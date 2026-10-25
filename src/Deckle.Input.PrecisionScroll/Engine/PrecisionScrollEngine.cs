using Deckle.Input;

namespace Deckle.Input.PrecisionScroll;

public sealed class PrecisionScrollEngine : IWheelInterceptor, IPrecisionScrollEngine
{
    // Long enough for an accepted gesture to play out its release, short
    // enough that a stuck worker shows up in the log before the user
    // notices a frozen settings toggle.
    private const int StopTimeoutMs = 2_000;

    private readonly IKeyboardInputHost _inputHost;
    private readonly WheelTickQueue _ticks = new();
    private readonly AutoResetEvent _wake = new(initialState: false);

    private PrecisionTouchpadInjector? _injector;
    private Thread? _worker;
    private PrecisionScrollTuning _tuning = new();
    private bool _scrollDirectionReversed;
    private int _accepting;
    private int _runRequested;
    private int _overflowed;
    private bool _disposed;
    private long _detents;
    private long _gestures;
    private long _rollovers;

    // Written by the worker before it exits on its own, read by Stop after
    // Join, which orders the two. Empty means the stop was requested.
    private string _stopReason = "";

    // Set by the worker as its last act, whatever ended it. _worker alone cannot
    // tell a running worker from one that already ended on its own, because only
    // Stop clears it.
    private int _workerEnded;

    public PrecisionScrollEngine(IKeyboardInputHost inputHost) => _inputHost = inputHost;

    public event Action<string>? EndedOnItsOwn;

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_worker is not null)
        {
            if (Volatile.Read(ref _workerEnded) == 0)
                return true;

            // The worker ended on its own and nobody stopped the engine since.
            // Reporting it as running would leave the wheel native with no way
            // back; reap it so this Start really starts again.
            Stop();
        }

        if (!PrecisionTouchpadInjector.TryCreate(out var injector, out int createError))
        {
            DecklePrecisionScrollSource.Log.Unavailable(
                PrecisionTouchpadInjector.IsSupported ? "create_failed" : "api_missing",
                createError,
                "",
                "");
            return false;
        }

        if (!PrecisionTouchpadSystemParameters.TryGetScrollDirectionReversed(
                out _scrollDirectionReversed,
                out int settingsError))
        {
            DecklePrecisionScrollSource.Log.TouchpadSettingsUnavailable(settingsError);
            injector!.Dispose();
            return false;
        }

        _injector = injector;
        _detents = 0;
        _gestures = 0;
        _rollovers = 0;
        _stopReason = "";
        Volatile.Write(ref _workerEnded, 0);
        Volatile.Write(ref _overflowed, 0);
        Volatile.Write(ref _runRequested, 1);
        Volatile.Write(ref _accepting, 1);

        Thread? worker = null;
        bool workerStarted = false;
        try
        {
            worker = new Thread(WorkerMain)
            {
                IsBackground = true,
                Name = "Deckle precision scroll",
            };
            worker.Start();
            workerStarted = true;
            _worker = worker;
            _inputHost.SetWheelInterceptor(this);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _accepting, 0);
            Volatile.Write(ref _runRequested, 0);
            _worker = null;
            if (workerStarted)
            {
                // The worker owns the injector once it runs and disposes it on
                // its way out; disposing here too would race that.
                _wake.Set();
                worker!.Join();
            }
            else
            {
                _injector?.Dispose();
            }
            _injector = null;
            DecklePrecisionScrollSource.Log.Unavailable(
                "thread_start_failed", 0, ex.GetType().Name, ex.Message);
            return false;
        }

        bool interceptionAvailable = _inputHost.IsWheelInterceptionAvailable;
        PrecisionScrollTuning tuning = Volatile.Read(ref _tuning);
        DecklePrecisionScrollSource.Log.EngineStarted(_scrollDirectionReversed, interceptionAvailable);
        DecklePrecisionScrollSource.Log.EngineStartedDetail(
            tuning.DistancePerDetentMm,
            tuning.InitialStepDurationMs,
            tuning.QuietPeriodScale,
            WheelTickQueue.Capacity);
        if (!interceptionAvailable)
            DecklePrecisionScrollSource.Log.Inactive("mouse_hook_missing");
        return true;
    }

    public void SetTuning(PrecisionScrollTuning tuning) =>
        Volatile.Write(ref _tuning, tuning.Normalize());

    public bool Intercept(in MouseWheelEvent wheelEvent)
    {
        if (Volatile.Read(ref _accepting) == 0 || !CanConvert(in wheelEvent))
            return false;

        int detents = wheelEvent.Delta / 120;
        if (!_ticks.TryEnqueue(new WheelTick(detents, wheelEvent.TimestampMs)))
        {
            Volatile.Write(ref _accepting, 0);
            Interlocked.Exchange(ref _overflowed, 1);
            _wake.Set();
            return false;
        }

        Interlocked.Add(ref _detents, Math.Abs(detents));
        _wake.Set();
        return true;
    }

    public void Stop()
    {
        Thread? worker = _worker;
        if (worker is null)
            return;

        Volatile.Write(ref _accepting, 0);
        _inputHost.SetWheelInterceptor(null);
        Volatile.Write(ref _runRequested, 0);
        _wake.Set();
        if (!worker.Join(StopTimeoutMs))
        {
            // Still waits: the worker owns the injector and its synthetic
            // contact, which must be released before a restart creates another.
            DecklePrecisionScrollSource.Log.StopSlow(StopTimeoutMs);
            worker.Join();
        }

        _worker = null;
        _injector = null;
        string reason = _stopReason;
        DecklePrecisionScrollSource.Log.EngineStopped(
            reason.Length == 0 ? "completed" : "failed",
            reason);
        DecklePrecisionScrollSource.Log.EngineStoppedDetail(_detents, _gestures, _rollovers);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Stop();
        _wake.Dispose();
        _disposed = true;
    }

    // The low-level hook is the only suppressible source, but it carries no
    // device handle, so the engine cannot tell two classic mice apart. Exact
    // classic detents are therefore the narrow boundary: Precision Touchpad
    // input uses finer deltas and remains native.
    //
    // Injected detents are accepted on purpose: Steam Input emits its
    // controller "scroll wheel" through injection, and a guard on the injected
    // flag left it dry. Deckle's own synthetic touchpad is not a recursion
    // risk: in the wheel captures its output never came back through this
    // hook, and a runaway would overflow the queue, which hands the wheel back
    // to Windows. Other injectors (remote tools, scripts) get smoothed too,
    // which is harmless.
    internal static bool CanConvert(in MouseWheelEvent wheelEvent) =>
        wheelEvent.Source == WheelEventSource.MessageHook
        && wheelEvent.Axis == WheelAxis.Vertical
        && wheelEvent.InputState == WheelInputState.None
        && wheelEvent.Delta != 0
        && wheelEvent.Delta % 120 == 0;

    private void WorkerMain()
    {
        try
        {
            RunWorker();
        }
        catch (Exception ex)
        {
            // The worker is an optional enhancement on a background thread: an
            // unexpected fault (the synthetic frame builder throws on an
            // impossible layout) hands the wheel back to Windows instead of
            // taking the whole process down with it.
            Volatile.Write(ref _accepting, 0);
            Volatile.Write(ref _runRequested, 0);
            _stopReason = "worker_faulted";
            _inputHost.SetWheelInterceptor(null);
            DecklePrecisionScrollSource.Log.WorkerFaulted(ex.GetType().Name, ex.Message);
        }
        finally
        {
            _injector?.Dispose();
        }

        Volatile.Write(ref _workerEnded, 1);
        string reason = _stopReason;
        if (reason.Length != 0)
            EndedOnItsOwn?.Invoke(reason);
    }

    private void RunWorker()
    {
        var gesture = new PrecisionScrollGesture();

        while (true)
        {
            gesture.SetTuning(Volatile.Read(ref _tuning));
            bool running = Volatile.Read(ref _runRequested) != 0;
            while (_ticks.TryDequeue(out WheelTick tick))
            {
                int contactDetents = _scrollDirectionReversed
                    ? -tick.Detents
                    : tick.Detents;
                gesture.AddDetents(contactDetents, tick.TimestampMs);
            }

            // Overload hands the wheel back to Windows: the interceptor detaches,
            // but the travel already accepted still plays out before the worker exits.
            if (Interlocked.Exchange(ref _overflowed, 0) != 0)
            {
                Volatile.Write(ref _runRequested, 0);
                running = false;
                _stopReason = "queue_full";
                _inputHost.SetWheelInterceptor(null);
                DecklePrecisionScrollSource.Log.QueueOverloaded("queue_full", WheelTickQueue.Capacity);
            }

            double nowMs = RawInputHost.NowMs;
            if (gesture.TryAdvance(nowMs, out PrecisionScrollFrame frame))
            {
                if (!Inject(frame))
                {
                    Volatile.Write(ref _accepting, 0);
                    Volatile.Write(ref _runRequested, 0);
                    _stopReason = "injection_failed";
                    _inputHost.SetWheelInterceptor(null);
                    break;
                }

                if (frame.Kind == PrecisionScrollFrameKind.Begin)
                    _gestures++;
                if (frame.Kind == PrecisionScrollFrameKind.End && frame.IsRollover)
                    _rollovers++;
                continue;
            }

            if (!running && !gesture.IsActive)
                break;

            _wake.WaitOne(gesture.GetWaitDurationMs(nowMs));
        }
    }

    private bool Inject(PrecisionScrollFrame frame)
    {
        PrecisionTouchpadInjector? injector = _injector;
        if (injector is null)
            return false;

        bool injected = frame.Kind switch
        {
            PrecisionScrollFrameKind.Begin => injector.Begin(frame.First, frame.Second),
            PrecisionScrollFrameKind.Move => injector.Move(frame.First, frame.Second, frame.ElapsedMs),
            PrecisionScrollFrameKind.End => injector.End(frame.ElapsedMs),
            _ => false,
        };

        if (injected)
            return true;

        DecklePrecisionScrollSource.Log.InjectionFailed(
            frame.Kind switch
            {
                PrecisionScrollFrameKind.Begin => "begin",
                PrecisionScrollFrameKind.Move => "move",
                PrecisionScrollFrameKind.End => "end",
                _ => "unknown",
            },
            injector.LastError);
        return false;
    }
}

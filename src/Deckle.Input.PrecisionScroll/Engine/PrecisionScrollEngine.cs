using Deckle.Input;

namespace Deckle.Input.PrecisionScroll;

public sealed class PrecisionScrollEngine : IWheelInterceptor, IDisposable
{
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

    public PrecisionScrollEngine(IKeyboardInputHost inputHost) => _inputHost = inputHost;

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_worker is not null)
            return true;

        if (!PrecisionTouchpadInjector.TryCreate(out var injector, out int createError))
        {
            DecklePrecisionScrollSource.Log.Unavailable();
            DecklePrecisionScrollSource.Log.UnavailableDetail(
                PrecisionTouchpadInjector.IsSupported ? "create_failed" : "api_missing",
                createError);
            return false;
        }

        if (!PrecisionTouchpadSystemParameters.TryGetScrollDirectionReversed(
                out _scrollDirectionReversed,
                out int settingsError))
        {
            DecklePrecisionScrollSource.Log.TouchpadSettingsUnavailable();
            DecklePrecisionScrollSource.Log.TouchpadSettingsUnavailableDetail(settingsError);
            injector!.Dispose();
            return false;
        }

        _injector = injector;
        _detents = 0;
        _gestures = 0;
        _rollovers = 0;
        Volatile.Write(ref _overflowed, 0);
        Volatile.Write(ref _runRequested, 1);
        Volatile.Write(ref _accepting, 1);

        try
        {
            _worker = new Thread(WorkerMain)
            {
                IsBackground = true,
                Name = "Deckle precision scroll",
            };
            _worker.Start();
            _inputHost.SetWheelInterceptor(this);
            DecklePrecisionScrollSource.Log.EngineStarted();
            return true;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _accepting, 0);
            Volatile.Write(ref _runRequested, 0);
            _worker = null;
            _injector?.Dispose();
            _injector = null;
            DecklePrecisionScrollSource.Log.Unavailable();
            DecklePrecisionScrollSource.Log.UnavailableDetail(ex.GetType().Name, 0);
            return false;
        }
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
        worker.Join();

        _worker = null;
        _injector = null;
        DecklePrecisionScrollSource.Log.EngineStopped();
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
    // device handle. Exact classic detents are therefore the narrow boundary:
    // measured Precision Touchpad input uses finer deltas and remains native.
    internal static bool CanConvert(in MouseWheelEvent wheelEvent) =>
        wheelEvent.Source == WheelEventSource.MessageHook
        && wheelEvent.Axis == WheelAxis.Vertical
        && !wheelEvent.IsInjected
        && wheelEvent.InputState == WheelInputState.None
        && wheelEvent.HasEquivalentTarget
        && wheelEvent.Delta != 0
        && wheelEvent.Delta % 120 == 0;

    private void WorkerMain()
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

            if (Interlocked.Exchange(ref _overflowed, 0) != 0)
            {
                Volatile.Write(ref _runRequested, 0);
                running = false;
                _inputHost.SetWheelInterceptor(null);
                DecklePrecisionScrollSource.Log.QueueOverloaded();
            }

            double nowMs = RawInputHost.NowMs;
            if (gesture.TryAdvance(nowMs, out PrecisionScrollFrame frame))
            {
                if (!Inject(frame))
                {
                    Volatile.Write(ref _accepting, 0);
                    Volatile.Write(ref _runRequested, 0);
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

        _injector?.Dispose();
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

        DecklePrecisionScrollSource.Log.InjectionFailed();
        DecklePrecisionScrollSource.Log.InjectionFailedDetail(
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

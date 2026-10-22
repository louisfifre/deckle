using Deckle.Input;

namespace Deckle.Input.PrecisionScroll;

// Keeps precision scrolling running while its switch is on. The app hands it each
// settings change; it owns the engine, the share of the input host the engine
// uses, and what to do when the worker ends on its own.
//
// The states, and what leaves each:
//   Off          the switch is off. On: start.
//   Running      the engine runs. Its worker ends on its own: restart once (below).
//   StartFailed  the switch is on but the engine could not start (the cause was
//                reported). Any settings change tries again.
//   Restarting   the worker ended on its own and one restart is waiting out its
//                delay. Nothing else starts the engine meanwhile.
//   StaysOff     the restart did not hold, or could not start. The wheel is
//                native and nothing retries: only the person toggling the switch
//                does, so a persistent fault cannot loop.
public sealed class PrecisionScrollRuntime : IDisposable
{
    // How long the wheel stays native before the one restart. The ends we
    // restart from have transient causes that last seconds: a refused frame
    // while the desktop switches (UAC prompt, lock screen, a full-screen app
    // taking input) and a burst of wheel ticks that filled the queue. Waiting
    // that out makes the restart likely to hold; waiting much longer would have
    // the person scroll natively for no reason. The value is a judgment, not a
    // measurement.
    internal static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    // An engine that ran at least this long before stopping did not fail
    // because of the restart, so its stop counts as a new first one and earns a
    // restart of its own. A shorter life means the fault came back at once: the
    // second stop keeps it off. Five minutes is well past a transient cause and
    // short enough that a fault returning an hour later is still treated as new.
    internal static readonly TimeSpan StableRun = TimeSpan.FromMinutes(5);

    private enum State { Off, Running, StartFailed, Restarting, StaysOff }

    private readonly Func<bool> _startHost;
    private readonly Action _stopHost;
    private readonly Func<IPrecisionScrollEngine> _createEngine;
    private readonly Action<string> _reportUnavailable;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<long> _nowMs;

    private readonly object _gate = new();
    private State _state = State.Off;
    private bool _enabled;
    private PrecisionScrollTuning _tuning = new();
    private IPrecisionScrollEngine? _engine;
    private long _engineStartedMs;
    private bool _restartUsed;
    private bool _failureReported;
    private string? _incidentReason;
    private CancellationTokenSource? _restartCts;

    // Bumped whenever the engine is replaced or stopped, so a late callback from
    // an engine that is no longer the current one finds out and does nothing.
    private int _generation;

    // Written without _gate (see OnEnded), read through Handling and Restarting.
    private Task _handling = Task.CompletedTask;
    private Task _restarting = Task.CompletedTask;

    // reportUnavailable is told, once per run of failures, why the switch is on
    // while nothing runs: host_start_failed or engine_start_failed.
    public PrecisionScrollRuntime(IKeyboardInputHost host, Action<string> reportUnavailable)
        : this(
            host.Start,
            host.Stop,
            () => new PrecisionScrollEngine(host),
            reportUnavailable,
            Task.Delay,
            static () => Environment.TickCount64)
    {
    }

    // The host, the engine and the clock are what a test cannot use for real: the
    // host hooks the whole desktop, the engine creates a synthetic touchpad, and
    // the delay takes seconds.
    internal PrecisionScrollRuntime(
        Func<bool> startHost,
        Action stopHost,
        Func<IPrecisionScrollEngine> createEngine,
        Action<string> reportUnavailable,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<long> nowMs)
    {
        _startHost = startHost;
        _stopHost = stopHost;
        _createEngine = createEngine;
        _reportUnavailable = reportUnavailable;
        _delay = delay;
        _nowMs = nowMs;
    }

    // Called for the settings as they are now, at start and on every change.
    public void Apply(bool enabled, PrecisionScrollTuning? tuning)
    {
        lock (_gate)
        {
            _tuning = tuning ?? new PrecisionScrollTuning();
            bool toggled = enabled != _enabled;
            _enabled = enabled;

            if (toggled)
            {
                // The person's own act: it ends a stay-off incident and gives the
                // next stop its restart back. A start cannot end the incident
                // before this: StaysOff is only left through here.
                CloseIncident();
                _restartUsed = false;
            }

            if (!enabled)
            {
                StopEngine();
                _state = State.Off;
                return;
            }

            switch (_state)
            {
                case State.Off:
                case State.StartFailed:
                    TryStart();
                    break;
                case State.Running:
                    _engine?.SetTuning(_tuning);
                    break;
                // Restarting and StaysOff keep the tuning for the next start and
                // wait: a slider moved while the wheel is native must not
                // restart what the policy decided to leave off.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _enabled = false;
            StopEngine();
            _state = State.Off;
        }
    }

    // The work that ran off the engine's worker thread, and the restart waiting
    // out its delay, so a test can wait for each at the point it expects it.
    internal Task Handling => Volatile.Read(ref _handling);
    internal Task Restarting => Volatile.Read(ref _restarting);

    // Brings an engine up, or reports why it could not. The state ends Running
    // or StartFailed. Called under _gate.
    private bool TryStart()
    {
        IPrecisionScrollEngine engine = _createEngine();
        engine.SetTuning(_tuning);
        int generation = ++_generation;
        engine.EndedOnItsOwn += reason => OnEnded(generation, reason);

        if (!_startHost())
        {
            // RawInputHost logged the cause; this names the switch that stays on
            // in Settings while nothing runs.
            Fail("host_start_failed");
            engine.Dispose();
            return false;
        }

        if (!engine.Start())
        {
            Fail("engine_start_failed");
            _stopHost();
            engine.Dispose();
            return false;
        }

        _engine = engine;
        _engineStartedMs = _nowMs();
        _state = State.Running;
        _failureReported = false;
        return true;
    }

    private void Fail(string reason)
    {
        _state = State.StartFailed;
        if (_failureReported) return;
        _failureReported = true;
        _reportUnavailable(reason);
    }

    // Disposes the engine and gives back its share of the host. Under _gate.
    private void StopEngine()
    {
        _restartCts?.Cancel();
        _restartCts = null;
        _generation++;

        IPrecisionScrollEngine? engine = _engine;
        _engine = null;
        if (engine is null) return;

        engine.Dispose();
        _stopHost();
    }

    // Raised on the engine's worker thread. It only hands over to the pool: the
    // work disposes the engine, which waits for that very thread to finish.
    // It must not take _gate either: whoever holds _gate may be waiting in
    // engine.Dispose() for this thread to end.
    private void OnEnded(int generation, string reason)
        => Volatile.Write(ref _handling, Task.Run(() => HandleEnded(generation, reason)));

    private void HandleEnded(int generation, string reason)
    {
        lock (_gate)
        {
            // Stopped by us already, or replaced: the end is not news.
            if (generation != _generation || _engine is null) return;

            long ranMs = _nowMs() - _engineStartedMs;
            StopEngine();
            if (ranMs >= StableRun.TotalMilliseconds)
                _restartUsed = false;

            if (_restartUsed)
            {
                StayOff(reason);
                return;
            }

            _restartUsed = true;
            _state = State.Restarting;
            DecklePrecisionScrollSource.Log.RestartScheduledDetail(
                reason, (long)RestartDelay.TotalMilliseconds);
            var cts = new CancellationTokenSource();
            _restartCts = cts;
            Volatile.Write(ref _restarting, RestartAfterDelayAsync(_generation, cts.Token));
        }
    }

    private async Task RestartAfterDelayAsync(int generation, CancellationToken ct)
    {
        try
        {
            await _delay(RestartDelay, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;   // the switch was toggled or the app is closing
        }

        lock (_gate)
        {
            if (ct.IsCancellationRequested || generation != _generation || _state != State.Restarting)
                return;

            // A restart that cannot start is the same outcome as one that does
            // not hold: the wheel stays native.
            if (!TryStart())
                StayOff("restart_failed");
        }
    }

    private void StayOff(string reason)
    {
        _state = State.StaysOff;
        _incidentReason = reason;
        DecklePrecisionScrollSource.Log.FellBackToNativeWheel(reason);
    }

    private void CloseIncident()
    {
        string? previous = _incidentReason;
        if (previous is null) return;
        _incidentReason = null;
        DecklePrecisionScrollSource.Log.FallbackRecovered(previous);
    }
}

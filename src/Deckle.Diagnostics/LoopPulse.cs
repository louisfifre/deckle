namespace Deckle.Diagnostics;

// Closed loop vocabulary of DeckleHotLoopSource: each pulse below is one loop
// that runs on its own cadence while its owner is alive, its name is the value
// of the loop field, and a call site ticks the pulse, never writes the name.
// The code that owns the loop's lifetime arms the pulse.
public static class HotLoops
{
    public static readonly LoopPulse WhispStreamingHeartbeat = new("whisp_streaming_heartbeat");
    public static readonly LoopPulse AnytypeHealthProbe = new("anytype_health_probe");
}

// Counts the ticks of one loop and turns a silence it should not have into one
// stall incident.
//
// Tick is one interlocked increment: it runs inside loops that fire many times
// a second, whether or not any listener is enabled, because a count that only
// exists while someone watches could not tell a stalled loop from an unwatched
// one. A single shared timer evaluates every pulse; it is created with the first
// pulse and costs one wake-up a minute.
public sealed class LoopPulse
{
    public const int WindowSeconds = 60;

    private static readonly object RegistryGate = new();
    private static readonly List<LoopPulse> Registry = [];
    private static Timer? s_timer;

    private readonly object _gate = new();
    private long _count;
    private int _armed;
    private long _lastTotal;
    private long _lastEvaluatedMs = Environment.TickCount64;
    private bool _expectedBefore;
    private bool _stalled;
    private long _stalledSinceMs;

    public LoopPulse(string loop)
    {
        Loop = loop;
        Register(this);
    }

    public string Loop { get; }

    // Ticks since the process started; LoopStalled carries it, so a reader
    // sees whether the loop ever worked.
    public long Total => Interlocked.Read(ref _count);

    public void Tick() => Interlocked.Increment(ref _count);

    // The loop is expected to tick for as long as the returned scope lives. Nested
    // and concurrent scopes add up: two streams are two reasons to expect ticks.
    public IDisposable Arm()
    {
        Interlocked.Increment(ref _armed);
        return new ArmedScope(this);
    }

    // One window: the stall when no tick arrived while the loop was expected in
    // this window and the one before (a loop that started a second ago has not
    // had its chance), the recovery when a stalled loop ticks again or is no
    // longer expected. `nowMs` is a parameter so a test moves the clock without
    // waiting.
    internal void Evaluate(long nowMs)
    {
        lock (_gate)
        {
            long total = Interlocked.Read(ref _count);
            long delta = total - _lastTotal;
            _lastTotal = total;
            long windowMs = nowMs - _lastEvaluatedMs;
            _lastEvaluatedMs = nowMs;

            bool expected = Volatile.Read(ref _armed) > 0;
            bool expectedBefore = _expectedBefore;
            _expectedBefore = expected;

            if (delta > 0)
            {
                if (_stalled) Settle("resumed", nowMs);
                return;
            }

            if (_stalled)
            {
                if (!expected) Settle("stopped", nowMs);
                return;
            }

            if (expected && expectedBefore)
            {
                _stalled = true;
                _stalledSinceMs = nowMs - windowMs;
                DeckleHotLoopSource.Log.LoopStalled(Loop, WindowSecondsOf(windowMs), total);
            }
        }
    }

    private static int WindowSecondsOf(long windowMs) => (int)Math.Round(windowMs / 1000.0);

    private void Settle(string reason, long nowMs)
    {
        _stalled = false;
        DeckleHotLoopSource.Log.LoopRecovered(Loop, reason, (int)Math.Round((nowMs - _stalledSinceMs) / 1000.0));
    }

    private static void Register(LoopPulse pulse)
    {
        lock (RegistryGate)
        {
            Registry.Add(pulse);
            s_timer ??= new Timer(
                static _ => EvaluateAll(),
                state: null,
                dueTime: TimeSpan.FromSeconds(WindowSeconds),
                period: TimeSpan.FromSeconds(WindowSeconds));
        }
    }

    private static void EvaluateAll()
    {
        long nowMs = Environment.TickCount64;
        LoopPulse[] pulses;
        lock (RegistryGate) pulses = [.. Registry];
        foreach (LoopPulse pulse in pulses)
        {
            try { pulse.Evaluate(nowMs); }
            catch (Exception) { /* a timer callback must never take the process down */ }
        }
    }

    private sealed class ArmedScope(LoopPulse owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref owner._armed);
        }
    }
}
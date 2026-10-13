namespace Deckle.Vision;

// Decides which acquired desktop frames the capture loop delivers so the
// delivery rate tracks a target rate.
//
// Why a deadline grid rather than "at least one interval since the last
// delivery". DXGI hands frames at the desktop's refresh boundaries with
// some jitter. With a minimum-gap rule, a 60 Hz target on a 60 Hz desktop
// drops every frame that arrives a fraction of a millisecond early, and a
// 144 Hz desktop is cut to every third frame (48 Hz). Advancing a deadline
// by one interval per delivery keeps the long-run average on the target;
// the tolerance (one eighth of the interval) absorbs arrival jitter.
//
// After a gap longer than one interval (static screen, recovery), the
// grid restarts from the current frame instead of delivering a burst to
// catch up.
//
// Pure arithmetic on Stopwatch-style ticks; not thread-safe, owned by the
// single capture loop.
internal sealed class CapturePacer
{
    private readonly long _intervalTicks;
    private readonly long _toleranceTicks;
    private long _nextDueTicks;
    private bool _started;

    public CapturePacer(int targetFrameRateHz, long ticksPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetFrameRateHz);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        _intervalTicks = ticksPerSecond / targetFrameRateHz;
        _toleranceTicks = _intervalTicks / 8;
    }

    public bool TryAdmit(long nowTicks)
    {
        if (!_started)
        {
            _started = true;
            _nextDueTicks = nowTicks + _intervalTicks;
            return true;
        }

        if (nowTicks < _nextDueTicks - _toleranceTicks)
        {
            return false;
        }

        _nextDueTicks += _intervalTicks;
        if (_nextDueTicks <= nowTicks)
        {
            _nextDueTicks = nowTicks + _intervalTicks;
        }
        return true;
    }
}

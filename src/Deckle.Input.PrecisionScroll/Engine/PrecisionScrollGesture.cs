using Deckle.Input;

namespace Deckle.Input.PrecisionScroll;

internal enum PrecisionScrollFrameKind
{
    Begin,
    Move,
    End,
}

internal readonly record struct PrecisionScrollFrame(
    PrecisionScrollFrameKind Kind,
    TouchpadPosition First,
    TouchpadPosition Second,
    uint ElapsedMs,
    bool IsRollover = false);

// Deterministic two-contact gesture model. Wheel ticks add physical travel;
// the worker samples that travel at the same 10 ms cadence as Microsoft's
// native injection sample. A backlog increases velocity naturally, while the
// bounded step keeps a burst from becoming a discontinuity.
internal sealed class PrecisionScrollGesture
{
    internal const int FrameIntervalMs = 10;
    internal const int ReleaseDelayMs = 40;
    internal const int TravelPerDetent = 1_200;

    private const int FirstContactX = 3_000;
    private const int SecondContactX = 7_000;
    private const int MinimumY = 1_000;
    private const int MaximumY = 5_000;
    private const int MinimumStep = 100;
    private const int MaximumStep = 360;
    private const double StepFraction = 0.22;

    private bool _active;
    private bool _endRequested;
    private int _direction;
    private int _y;
    private double _pendingTravel;
    private double _lastTickMs;
    private double _lastFrameMs;
    private double _nextFrameMs;

    public bool IsActive => _active;

    public void AddDetent(int direction, double sensitivity, double timestampMs)
    {
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));

        double travel = direction * TravelPerDetent * Math.Clamp(sensitivity, 0.5, 2.0);

        if (_active && direction != _direction)
        {
            if (Math.Sign(_pendingTravel) != direction)
                _pendingTravel = 0;
            _endRequested = true;
        }
        else if (_active && _endRequested)
        {
            _pendingTravel = 0;
            _endRequested = false;
        }
        else if (!_active && _pendingTravel != 0 && Math.Sign(_pendingTravel) != direction)
        {
            _pendingTravel = 0;
        }

        _pendingTravel += travel;
        _lastTickMs = timestampMs;
    }

    public void RequestEnd()
    {
        _pendingTravel = 0;
        _endRequested = _active;
    }

    public bool TryAdvance(double nowMs, out PrecisionScrollFrame frame)
    {
        if (_endRequested && _active)
        {
            frame = End(nowMs, isRollover: false);
            _endRequested = false;
            return true;
        }

        if (!_active)
        {
            if (_pendingTravel == 0)
            {
                frame = default;
                return false;
            }

            _direction = Math.Sign(_pendingTravel);
            _y = _direction > 0 ? MinimumY : MaximumY;
            _active = true;
            _lastFrameMs = nowMs;
            _nextFrameMs = nowMs + FrameIntervalMs;
            frame = CreateFrame(PrecisionScrollFrameKind.Begin, elapsedMs: 0);
            return true;
        }

        if (_pendingTravel != 0)
        {
            if (nowMs < _nextFrameMs)
            {
                frame = default;
                return false;
            }

            if (Math.Sign(_pendingTravel) != _direction)
            {
                frame = End(nowMs, isRollover: false);
                return true;
            }

            int step = NextStep(_pendingTravel);
            int nextY = _y + step;
            if (nextY is < MinimumY or > MaximumY)
            {
                frame = End(nowMs, isRollover: true);
                return true;
            }

            _y = nextY;
            _pendingTravel -= step;
            if (Math.Abs(_pendingTravel) < 1 || Math.Sign(_pendingTravel) != _direction)
                _pendingTravel = 0;

            uint elapsedMs = ElapsedSinceLastFrame(nowMs);
            _lastFrameMs = nowMs;
            _nextFrameMs = nowMs + FrameIntervalMs;
            frame = CreateFrame(PrecisionScrollFrameKind.Move, elapsedMs);
            return true;
        }

        double releaseAt = Math.Max(_nextFrameMs, _lastTickMs + ReleaseDelayMs);
        if (nowMs < releaseAt)
        {
            frame = default;
            return false;
        }

        frame = End(nowMs, isRollover: false);
        return true;
    }

    public int GetWaitDurationMs(double nowMs)
    {
        if (_endRequested || (!_active && _pendingTravel != 0))
            return 0;
        if (!_active)
            return Timeout.Infinite;

        double dueAt = _pendingTravel != 0
            ? _nextFrameMs
            : Math.Max(_nextFrameMs, _lastTickMs + ReleaseDelayMs);
        return dueAt <= nowMs ? 0 : (int)Math.Ceiling(dueAt - nowMs);
    }

    private PrecisionScrollFrame End(double nowMs, bool isRollover)
    {
        uint elapsedMs = ElapsedSinceLastFrame(nowMs);
        var frame = CreateFrame(PrecisionScrollFrameKind.End, elapsedMs, isRollover);
        _active = false;
        _direction = 0;
        _nextFrameMs = nowMs;
        return frame;
    }

    private PrecisionScrollFrame CreateFrame(
        PrecisionScrollFrameKind kind,
        uint elapsedMs,
        bool isRollover = false) =>
        new(
            kind,
            new TouchpadPosition(FirstContactX, _y),
            new TouchpadPosition(SecondContactX, _y),
            elapsedMs,
            isRollover);

    private uint ElapsedSinceLastFrame(double nowMs) =>
        (uint)Math.Clamp(Math.Round(nowMs - _lastFrameMs), 1, uint.MaxValue);

    private static int NextStep(double pendingTravel)
    {
        double magnitude = Math.Clamp(
            Math.Abs(pendingTravel) * StepFraction,
            MinimumStep,
            MaximumStep);
        magnitude = Math.Min(magnitude, Math.Abs(pendingTravel));
        int rounded = Math.Max(1, (int)Math.Round(magnitude, MidpointRounding.AwayFromZero));
        return Math.Sign(pendingTravel) * rounded;
    }
}

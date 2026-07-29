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

// Each same-direction detent adds an exact travel budget. A direction change
// deliberately cancels unfinished travel before the opposite detent starts:
// reversal stays immediate instead of leaking old-direction motion. Recent
// inter-detent gaps determine how quickly the current budget is delivered. A
// stationary frame precedes the final lift so stopping adds no synthetic
// inertia after the wheel itself has stopped.
internal sealed class PrecisionScrollGesture
{
    internal const int FrameIntervalMs = 10;

    private const int MinimumY = 0;
    private const int MaximumY = PrecisionTouchpadInjector.DeviceHeight - 1;
    private const int ContactHalfSpacing = 2_000;
    private const double HimetricPerMillimeter = 100;

    private readonly double[] _gapsMs = new double[3];

    private PrecisionScrollTuning _tuning;
    private bool _active;
    private bool _queued;
    private bool _endRequested;
    private bool _rolloverRequested;
    private bool _stationaryFrameSent;
    private int _direction;
    private int _queuedDirection;
    private int _gapCount;
    private int _gapIndex;
    private int _y;
    private double _exactY;
    private double _remainingTravel;
    private double _speed;
    private double _lastTickMs;
    private double _lastFrameMs;
    private double _nextFrameMs;
    private double _queuedTravel;
    private double _queuedSpeed;
    private double _queuedGapMs;
    private double _queuedLastTickMs;

    public bool IsActive => _active;

    public PrecisionScrollGesture(PrecisionScrollTuning? tuning = null) =>
        _tuning = (tuning ?? new PrecisionScrollTuning()).Normalize();

    public void SetTuning(PrecisionScrollTuning tuning) =>
        _tuning = tuning.Normalize();

    public void AddDetents(int detents, double timestampMs)
    {
        if (detents == 0)
            throw new ArgumentOutOfRangeException(nameof(detents));

        if (!_active)
        {
            QueueInput(detents, timestampMs);
            return;
        }

        int direction = Math.Sign(detents);
        double gapMs = timestampMs - _lastTickMs;
        if (direction != _direction)
        {
            _direction = direction;
            _remainingTravel = 0;
            _speed = 0;
            _endRequested = false;
            _rolloverRequested = false;
            ResetGapHistory();
        }
        else if (_endRequested && !_rolloverRequested)
        {
            _endRequested = false;
        }

        if (gapMs > 0)
            AddGap(gapMs);

        double travel = TravelForDetents(detents);
        _remainingTravel += travel;
        _speed = Math.Sign(travel) * Math.Abs(travel) / EstimatedGapMs();
        _lastTickMs = timestampMs;
        _stationaryFrameSent = false;
    }

    public bool TryAdvance(double nowMs, out PrecisionScrollFrame frame)
    {
        if (_endRequested && _active)
        {
            frame = End(nowMs);
            return true;
        }

        if (!_active)
        {
            if (!_queued)
            {
                frame = default;
                return false;
            }

            BeginQueued(nowMs);
            frame = CreateFrame(PrecisionScrollFrameKind.Begin, elapsedMs: 0);
            return true;
        }

        if (nowMs < _nextFrameMs)
        {
            frame = default;
            return false;
        }

        double elapsedMs = Math.Max(1, nowMs - _lastFrameMs);
        double movement = NextMovement(elapsedMs);
        double edge = _direction > 0 ? MaximumY : MinimumY;
        double available = Math.Abs(edge - _exactY);
        double applied = Math.Min(Math.Abs(movement), available);
        if (applied > 0)
        {
            double signedApplied = Math.CopySign(applied, movement);
            _exactY += signedApplied;
            _remainingTravel -= signedApplied;
            if (Math.Abs(_remainingTravel) < 0.5)
                _remainingTravel = 0;
        }

        if (Math.Abs(movement) > available || (_remainingTravel != 0 && available == 0))
        {
            _rolloverRequested = true;
            _endRequested = true;
        }

        if (_remainingTravel == 0)
            _speed = 0;

        bool stationary = applied == 0;
        _stationaryFrameSent = stationary;
        _y = (int)Math.Round(_exactY, MidpointRounding.AwayFromZero);
        uint frameElapsedMs = ElapsedSinceLastFrame(nowMs);
        _lastFrameMs = nowMs;
        _nextFrameMs = nowMs + FrameIntervalMs;
        frame = CreateFrame(
            PrecisionScrollFrameKind.Move,
            frameElapsedMs,
            isRollover: _rolloverRequested);

        if (!_rolloverRequested
            && _remainingTravel == 0
            && _stationaryFrameSent
            && nowMs >= _lastTickMs + QuietDurationMs())
        {
            _endRequested = true;
        }

        return true;
    }

    public int GetWaitDurationMs(double nowMs)
    {
        if (_endRequested || _queued)
            return 0;
        if (_active)
            return DueInMs(_nextFrameMs, nowMs);
        return Timeout.Infinite;
    }

    private void QueueInput(int detents, double timestampMs)
    {
        int direction = Math.Sign(detents);
        double travel = TravelForDetents(detents);

        if (_queued && direction == _queuedDirection)
        {
            double gapMs = timestampMs - _queuedLastTickMs;
            if (gapMs > 0)
                _queuedGapMs = gapMs;
            _queuedTravel += travel;
            _queuedSpeed = Math.Sign(travel) * Math.Abs(travel)
                / Math.Max(_queuedGapMs, 1);
        }
        else
        {
            _queued = true;
            _queuedDirection = direction;
            _queuedTravel = travel;
            _queuedGapMs = _tuning.InitialStepDurationMs;
            _queuedSpeed = Math.Sign(travel) * Math.Abs(travel)
                / _queuedGapMs;
        }

        _queuedLastTickMs = timestampMs;
    }

    private void BeginQueued(double nowMs)
    {
        _direction = _queuedDirection;
        _remainingTravel = _queuedTravel;
        _speed = _queuedSpeed;
        _lastTickMs = _queuedLastTickMs;
        _queued = false;
        _queuedTravel = 0;
        _active = true;
        _endRequested = false;
        _rolloverRequested = false;
        _stationaryFrameSent = false;
        ResetGapHistory();
        if (_queuedGapMs != _tuning.InitialStepDurationMs)
            AddGap(_queuedGapMs);
        _y = _direction > 0 ? MinimumY : MaximumY;
        _exactY = _y;
        _lastFrameMs = nowMs;
        _nextFrameMs = nowMs + FrameIntervalMs;
    }

    private PrecisionScrollFrame End(double nowMs)
    {
        if (_rolloverRequested && Math.Abs(_remainingTravel) >= 0.5)
        {
            _queued = true;
            _queuedDirection = _direction;
            _queuedTravel = _remainingTravel;
            _queuedSpeed = _speed;
            _queuedGapMs = EstimatedGapMs();
            _queuedLastTickMs = _lastTickMs;
        }

        uint elapsedMs = ElapsedSinceLastFrame(nowMs);
        var frame = CreateFrame(
            PrecisionScrollFrameKind.End,
            elapsedMs,
            isRollover: _rolloverRequested);

        _active = false;
        _endRequested = false;
        _rolloverRequested = false;
        _stationaryFrameSent = false;
        _direction = 0;
        _remainingTravel = 0;
        _speed = 0;
        _nextFrameMs = nowMs;
        return frame;
    }

    private double NextMovement(double elapsedMs)
    {
        if (_remainingTravel == 0 || _speed == 0)
            return 0;

        double magnitude = Math.Min(
            Math.Abs(_remainingTravel),
            Math.Abs(_speed) * elapsedMs);
        return Math.CopySign(magnitude, _remainingTravel);
    }

    private void AddGap(double gapMs)
    {
        _gapsMs[_gapIndex] = gapMs;
        _gapIndex = (_gapIndex + 1) % _gapsMs.Length;
        _gapCount = Math.Min(_gapCount + 1, _gapsMs.Length);
    }

    private void ResetGapHistory()
    {
        _gapCount = 0;
        _gapIndex = 0;
    }

    private double EstimatedGapMs() => _gapCount switch
    {
        0 => _tuning.InitialStepDurationMs,
        1 => _gapsMs[0],
        2 => (_gapsMs[0] + _gapsMs[1]) / 2,
        _ => _gapsMs[0] + _gapsMs[1] + _gapsMs[2]
            - Math.Min(_gapsMs[0], Math.Min(_gapsMs[1], _gapsMs[2]))
            - Math.Max(_gapsMs[0], Math.Max(_gapsMs[1], _gapsMs[2])),
    };

    private double QuietDurationMs() => Math.Clamp(
        EstimatedGapMs() * _tuning.QuietPeriodScale,
        FrameIntervalMs * 2,
        _tuning.InitialStepDurationMs * 3);

    private double TravelForDetents(int detents) =>
        detents * _tuning.DistancePerDetentMm * HimetricPerMillimeter;

    private PrecisionScrollFrame CreateFrame(
        PrecisionScrollFrameKind kind,
        uint elapsedMs,
        bool isRollover = false) =>
        new(
            kind,
            new TouchpadPosition(
                PrecisionTouchpadInjector.DeviceWidth / 2 - ContactHalfSpacing,
                _y),
            new TouchpadPosition(
                PrecisionTouchpadInjector.DeviceWidth / 2 + ContactHalfSpacing,
                _y),
            elapsedMs,
            isRollover);

    private static int DueInMs(double dueAt, double nowMs) =>
        dueAt <= nowMs ? 0 : (int)Math.Ceiling(dueAt - nowMs);

    private uint ElapsedSinceLastFrame(double nowMs) =>
        (uint)Math.Clamp(Math.Round(nowMs - _lastFrameMs), 1, uint.MaxValue);
}

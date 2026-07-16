namespace Deckle.Lighting;

// Owns the durable state of one EventStream loss episode. It is intentionally
// independent from HTTP so the warning grace period and one-shot recovery can
// be tested without a bridge or wall-clock delays.
internal sealed class HueEventStreamEpisode
{
    internal static readonly TimeSpan IncidentDelay = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;

    private long _generation;
    private long _lostAtTimestamp;
    private int _failureCount;
    private Exception? _lastException;
    private bool _disconnected;
    private bool _incidentOpen;

    internal HueEventStreamEpisode(TimeProvider? timeProvider = null)
        => _timeProvider = timeProvider ?? TimeProvider.System;

    internal HueEventStreamLoss RecordLoss(Exception? exception)
    {
        lock (_gate)
        {
            if (!_disconnected)
            {
                _disconnected = true;
                _incidentOpen = false;
                _lostAtTimestamp = _timeProvider.GetTimestamp();
                _failureCount = 1;
                _lastException = exception;
                _generation++;
                return new HueEventStreamLoss(Started: true, _generation);
            }

            _failureCount++;
            _lastException = exception;
            return new HueEventStreamLoss(Started: false, _generation);
        }
    }

    internal bool TryOpenIncident(long generation, out HueEventStreamObservation observation)
    {
        lock (_gate)
        {
            TimeSpan duration = GetDuration();
            if (!_disconnected
                || _incidentOpen
                || generation != _generation
                || duration < IncidentDelay)
            {
                observation = default;
                return false;
            }

            _incidentOpen = true;
            observation = new HueEventStreamObservation(duration, _failureCount, _lastException);
            return true;
        }
    }

    internal bool TryRecover(out HueEventStreamObservation observation)
    {
        lock (_gate)
        {
            if (!_disconnected)
            {
                observation = default;
                return false;
            }

            bool shouldReport = _incidentOpen;
            observation = new HueEventStreamObservation(
                GetDuration(),
                _failureCount,
                _lastException);

            _disconnected = false;
            _incidentOpen = false;
            _failureCount = 0;
            _lastException = null;
            return shouldReport;
        }
    }

    private TimeSpan GetDuration()
        => _timeProvider.GetElapsedTime(_lostAtTimestamp, _timeProvider.GetTimestamp());
}

internal readonly record struct HueEventStreamLoss(bool Started, long Generation);

internal readonly record struct HueEventStreamObservation(
    TimeSpan Duration,
    int FailureCount,
    Exception? LastException);

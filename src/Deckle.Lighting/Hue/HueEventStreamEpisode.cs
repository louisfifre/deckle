using Deckle.Diagnostics;

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
            // The tray follows the incident under this lock: the opening runs
            // on its own task after the grace period, and a reconnection at
            // that very moment must not close the tray before it opens.
            UserIncidentRegistry.Instance.Open(
                UserIncidents.HueEvents, _lastException?.GetType().Name ?? "clean_close");
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
            if (shouldReport) UserIncidentRegistry.Instance.Close(UserIncidents.HueEvents);

            _disconnected = false;
            _incidentOpen = false;
            _failureCount = 0;
            _lastException = null;
            return shouldReport;
        }
    }

    // Ends an open incident because the subscriber itself stopped. A loss that
    // never reached the incident delay has nothing to close.
    internal bool TryAbandon(out HueEventStreamObservation observation)
    {
        lock (_gate)
        {
            bool shouldReport = _disconnected && _incidentOpen;
            observation = shouldReport
                ? new HueEventStreamObservation(GetDuration(), _failureCount, _lastException)
                : default;
            // A stopped subscriber has no bridge to reach any more.
            if (shouldReport) UserIncidentRegistry.Instance.Close(UserIncidents.HueEvents);

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

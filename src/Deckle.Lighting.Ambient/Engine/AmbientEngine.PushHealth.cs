using System.Diagnostics;
using System.Net;
using Deckle.Diagnostics;
using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    private int GetPushDelayMs()
    {
        if (_pushConsecutiveFailures == 0) return _pushIntervalMs;
        if (_pushFailureStartTimestamp == 0) return 1000;

        long elapsedMs = (Stopwatch.GetTimestamp() - _pushFailureStartTimestamp)
            * 1000 / Stopwatch.Frequency;
        return elapsedMs >= 30_000 ? 5000 : 1000;
    }

    private void OnPushFailed(Exception ex)
    {
        _pushConsecutiveFailures++;
        if (_pushFailureStartTimestamp == 0)
            _pushFailureStartTimestamp = Stopwatch.GetTimestamp();
        _pushFailureType = ex.GetType().Name;
        _pushFailureMessage = ex.Message;
        _hbPushFailures++;

        if (IsTerminalPushFailure(ex))
        {
            long durationMs = PushFailureDurationMs();
            int failures = _pushConsecutiveFailures;
            string failureType = _pushFailureType;
            string failureMessage = _pushFailureMessage;
            int status = ex is HttpRequestException { StatusCode: { } code } ? (int)code : 0;

            // AbortStartOrStop rather than a bare Stop: a rejection on the
            // first ticks lands before IsRunning, where Stop is a no-op.
            AbortStartOrStop("push_rejected", () =>
            {
                DeckleAmbientSource.Log.PushRejected(status, failureType, failureMessage);
                DeckleAmbientSource.Log.PushEpisodeDetail(
                    "terminal", failures, durationMs, failureType, failureMessage);
            });
            return;
        }

        DeckleAmbientSource.Log.PushAttemptFailedDetail(
            _pushConsecutiveFailures, GetPushDelayMs(), _pushFailureType, _pushFailureMessage);

        if (_pushConsecutiveFailures == 3)
        {
            _pushIncidentOpen = true;
            DeckleAmbientSource.Log.PushIncidentOpened(
                _pushConsecutiveFailures, _pushFailureType, _pushFailureMessage);
            DeckleAmbientSource.Log.PushEpisodeDetail(
                "opened", _pushConsecutiveFailures, PushFailureDurationMs(),
                _pushFailureType, _pushFailureMessage);
        }
    }

    private void OnPushSucceeded()
    {
        if (_pushConsecutiveFailures == 0) return;

        if (_pushIncidentOpen)
        {
            int failures = _pushConsecutiveFailures;
            long durationMs = PushFailureDurationMs();
            string failureType = _pushFailureType;
            string failureMessage = _pushFailureMessage;
            DeckleAmbientSource.Log.PushRecovered();
            DeckleAmbientSource.Log.PushEpisodeDetail(
                "recovered", failures, durationMs, failureType, failureMessage);
        }

        ResetPushFailureEpisode();
    }

    private long PushFailureDurationMs()
        => _pushFailureStartTimestamp == 0
            ? 0
            : (Stopwatch.GetTimestamp() - _pushFailureStartTimestamp)
                * 1000 / Stopwatch.Frequency;

    private static bool IsTerminalPushFailure(Exception ex)
        => ex is InvalidOperationException
            or HttpRequestException
            {
                StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            };

    private void ResetPushFailureEpisode()
    {
        _pushConsecutiveFailures = 0;
        _pushFailureStartTimestamp = 0;
        _pushIncidentOpen = false;
        _pushFailureType = "none";
        _pushFailureMessage = "none";
    }

    private static string FormatPushedColors(Dictionary<string, LightColor> pushed)
    {
        var sb = new System.Text.StringBuilder(pushed.Count * 18);
        bool first = true;
        foreach (var (id, c) in pushed)
        {
            if (!first) sb.Append(' ');
            sb.Append(id).Append('=').Append(c.R).Append(',').Append(c.G).Append(',').Append(c.B);
            first = false;
        }
        return sb.ToString();
    }
}

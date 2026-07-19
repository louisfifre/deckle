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

        if (IsTerminalPushFailure(ex))
        {
            long durationMs = PushFailureDurationMs();
            DeckleAmbientSource.Log.PushRejected();
            DeckleAmbientSource.Log.PushEpisodeDetail(
                "terminal", _pushConsecutiveFailures, durationMs,
                _pushFailureType, _pushFailureMessage);
            _stopReason = "push_rejected";
            _ = Task.Run(Stop);
            return;
        }

        if (_pushConsecutiveFailures == 3)
        {
            _pushIncidentOpen = true;
            DeckleAmbientSource.Log.PushIncidentOpened();
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

    // Format the per-light colour set as "id=R,G,B id=R,G,B …" for
    // the push log. Short enough to fit on one line for 3-5 lamps ;
    // longer setups will wrap but stay readable.
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

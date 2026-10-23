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

    // internal: the tests drive the push-health episode without a bridge.
    internal void OnPushFailed(Exception ex)
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
                // The rejection is this incident's end; the stop must not close it again.
                _pushIncidentOpen = false;
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
            // The lights stopped following the screen: the tray shows it until
            // a push lands or the run ends (ResetPushFailureEpisode).
            UserIncidentRegistry.Instance.Open(UserIncidents.HuePush, _pushFailureType);
        }
    }

    internal void OnPushSucceeded()
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

    // Called once the run's loops and capture are gone, so no producer can
    // still touch the episodes. An incident the run opened and never saw
    // recover is closed here, once. The terminal events (PushRejected,
    // FrameProcessingFailed) end their incident themselves and mark it, so they
    // never double up with this one.
    internal void AbandonOpenIncidents(string runId)
    {
        if (_pushIncidentOpen)
        {
            DeckleAmbientSource.Log.PipelineIncidentAbandoned(
                runId, AmbientIncidentKinds.PushFailures,
                _pushConsecutiveFailures, PushFailureDurationMs());
        }
        ResetPushFailureEpisode();

        if (_frameProcessingIncidentOpen && !_frameProcessingIncidentEnded)
        {
            DeckleAmbientSource.Log.PipelineIncidentAbandoned(
                runId, AmbientIncidentKinds.FrameProcessing,
                _frameProcessingFailures,
                _frameProcessingActiveFailureTicks * 1000L / Stopwatch.Frequency);
        }
        ResetFrameProcessingEpisode();
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

    // Every way out of an episode passes here: the recovery, the end of the
    // run (the abandonment, or a rejection that stopped it) and the next start.
    // A stopped run has no lights to reach, so its incident is no longer the
    // person's to fix. Closing what is not open changes nothing.
    private void ResetPushFailureEpisode()
    {
        UserIncidentRegistry.Instance.Close(UserIncidents.HuePush);
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

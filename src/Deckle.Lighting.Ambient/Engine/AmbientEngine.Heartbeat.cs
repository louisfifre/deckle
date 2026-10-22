using System.Diagnostics;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    private void MaybeEmitHeartbeat()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = _heartbeatWindow.ElapsedTicks(now) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs < HeartbeatIntervalMs) return;

        // Push stats over the elapsed window, as typed fields so a reader
        // can aggregate them. They stay 0 when no push happened (static
        // screen); pushed=0 on the same line says the zeros mean "no
        // sample", not "instant pushes".
        double avg = 0, p95 = 0, max = 0;
        if (_hbPushDurationsMs is { Count: > 0 })
        {
            double sum = 0;
            foreach (var v in _hbPushDurationsMs)
            {
                if (v > max) max = v;
                sum += v;
            }
            avg = sum / _hbPushDurationsMs.Count;
            var sorted = _hbPushDurationsMs.ToArray();
            Array.Sort(sorted);
            int p95Idx = Math.Max(0, Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1));
            p95 = sorted[p95Idx];
        }

        // Counters reset whether or not the producer admits the line, so
        // the next window starts from zero.
        DeckleAmbientSource.Log.Heartbeat(
            _multiLightActive ? "multi" : "group",
            elapsedMs / 1000.0,
            _pushRateHz,
            _hbTicks * 1000.0 / elapsedMs,
            _hbTicks,
            _hbPushed,
            _hbDropped,
            _hbSkippedSlots,
            _multiLightActive ? _hbUnmappedLights : 0,
            _hbPushFailures,
            avg,
            p95,
            max);

        _heartbeatWindow.Restart(now);
        ResetHeartbeatCounters();
    }

    private bool HasHeartbeatObservations()
        => _hbTicks != 0 || _hbPushed != 0 || _hbDropped != 0
        || _hbUnmappedLights != 0 || _hbSkippedSlots != 0 || _hbPushFailures != 0
        || _hbPushDurationsMs is { Count: > 0 };

    private void ResetHeartbeatCounters()
    {
        _hbTicks = _hbPushed = _hbDropped = _hbUnmappedLights = _hbPushFailures = 0;
        _hbSkippedSlots = 0;
        _hbPushDurationsMs?.Clear();
    }
}

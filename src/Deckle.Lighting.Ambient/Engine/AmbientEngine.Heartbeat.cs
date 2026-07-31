using System.Diagnostics;
using Deckle.Diagnostics;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    private void MaybeEmitHeartbeat()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - _hbTimestamp) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs < HeartbeatIntervalMs) return;

        // Push stats over the elapsed window. Skipped from the line
        // when no push happened in the window (static screen) — the
        // ticks=N pushed=0 prefix already says "loop alive, nothing
        // to push", a "push_avg_ms=0.0" suffix would be misleading.
        string pushStats = "";
        if (_hbPushDurationsMs is { Count: > 0 })
        {
            double min = double.MaxValue, max = 0, sum = 0;
            foreach (var v in _hbPushDurationsMs)
            {
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
            }
            double avg = sum / _hbPushDurationsMs.Count;
            var sorted = _hbPushDurationsMs.ToArray();
            Array.Sort(sorted);
            int p95Idx = Math.Max(0, Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1));
            double p95 = sorted[p95Idx];
            pushStats = $" | push_avg_ms={avg:F1} | push_p95_ms={p95:F1} | push_max_ms={max:F1}";
        }

        // Per-tick Verbose: admitted by the producer from activity scope plus
        // user toggle. Counters are reset whether
        // the line was emitted or not, so the next heartbeat window
        // starts from zero — the metric stays correct when the
        // toggle flips mid-session.
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
            pushStats);

        ResetHeartbeatWindow(now);
    }

    private void ResetHeartbeatWindow(long? timestamp = null)
    {
        _hbTimestamp = timestamp ?? Stopwatch.GetTimestamp();
        _hbTicks = _hbPushed = _hbDropped = _hbUnmappedLights = 0;
        _hbSkippedSlots = 0;
        _hbPushDurationsMs?.Clear();
    }
}

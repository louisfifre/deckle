using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net;
using Deckle.Diagnostics;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    private async Task PushLoopAsync(CancellationToken ct)
    {
        // The loop runs on the thread-pool and serialises downstream pushes.
        // Its monotonic deadline skips expired slots instead of replaying
        // stale colours in a catch-up burst. Any exception on a single tick's
        // push is absorbed into the push-health episode so a transient bridge
        // failure does not kill the loop — the next scheduled tick retries.
        try
        {
            long intervalTicks = Math.Max(1, Stopwatch.Frequency / _pushRateHz);
            long nextDeadline = Stopwatch.GetTimestamp();

            while (!ct.IsCancellationRequested)
            {
                long tickStartedAt = Stopwatch.GetTimestamp();

                // Refresh the tuning snapshot from the host. Cheap
                // property reads on the singleton settings give UI
                // edits a one-tick reaction window without a restart.
                var ambient = _host.Ambient;
                _sampler!.SetExposureEv(ambient.ExposureEv);
                _saturationBoost       = ambient.SaturationBoost;
                _brightnessCurveX1     = ambient.BrightnessCurveX1;
                _brightnessCurveY1     = ambient.BrightnessCurveY1;
                _brightnessCurveX2     = ambient.BrightnessCurveX2;
                _brightnessCurveY2     = ambient.BrightnessCurveY2;
                _minBrightnessEnabled  = ambient.MinBrightnessEnabled;
                _minBrightness         = ambient.MinBrightness;
                _changeThreshold       = ambient.ChangeThreshold;
                _smoothingAlpha        = ambient.SmoothingAlpha;
                _borderMode            = ambient.BorderMode;
                _borderDepth           = ambient.BorderDepth;
                _borderCells           = ambient.BorderCells;

                var sample = _sampler!.LatestSample;
                if (sample is null)
                {
                    // Sampler hasn't produced a frame yet (the first
                    // capture frame after Start). Wait one push interval
                    // and retry.
                    await Task.Delay(_pushIntervalMs, ct).ConfigureAwait(false);
                    nextDeadline = Stopwatch.GetTimestamp();
                    continue;
                }

                // A continuous stream pushes even when the target colour is
                // unchanged. Per-push detail would therefore create 50 log
                // lines/s on Entertainment and distort a soak test. The
                // heartbeat below retains the useful aggregate measurements;
                // persistent outputs keep their sparse change-level detail.
                bool pushDetailEnabled = !_requiresContinuousColorUpdates
                    && OperationalLogAdmission.IsDetailEnabled(
                        OperationalLogActivity.Ambient,
                        DeckleAmbientSource.Log,
                        EventLevel.Verbose,
                        (EventKeywords)Keywords.Push);
                bool heartbeatDetailEnabled = OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleAmbientSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Heartbeat);

                if (heartbeatDetailEnabled)
                {
                    if (_heartbeatWindow.StartIfNeeded(Stopwatch.GetTimestamp()))
                        ResetHeartbeatCounters();
                }
                else
                {
                    _heartbeatWindow.Stop();
                    if (HasHeartbeatObservations())
                        ResetHeartbeatCounters();
                }

                long tickTimestamp = Stopwatch.GetTimestamp();
                _tickDtSeconds = _lastTickTimestamp != 0
                    ? (tickTimestamp - _lastTickTimestamp) / (double)Stopwatch.Frequency
                    : 1.0 / _pushRateHz;
                _lastTickTimestamp = tickTimestamp;

                if (_multiLightActive)
                {
                    await MultiLightTickAsync(
                        sample, ct, pushDetailEnabled, heartbeatDetailEnabled)
                        .ConfigureAwait(false);
                }
                else
                {
                    await GroupTickAsync(
                        sample, ct, pushDetailEnabled, heartbeatDetailEnabled)
                        .ConfigureAwait(false);
                }

                if (heartbeatDetailEnabled)
                {
                    _hbTicks++;
                    MaybeEmitHeartbeat();
                }

                int pushDelayMs = GetPushDelayMs();
                if (pushDelayMs != _pushIntervalMs)
                {
                    await Task.Delay(pushDelayMs, ct).ConfigureAwait(false);
                    nextDeadline = Stopwatch.GetTimestamp();
                    continue;
                }

                nextDeadline = AmbientPushCadence.AdvanceDeadline(
                    nextDeadline,
                    tickStartedAt,
                    Stopwatch.GetTimestamp(),
                    intervalTicks,
                    out long skippedSlots);
                if (heartbeatDetailEnabled)
                    _hbSkippedSlots += skippedSlots;

                long remainingTicks = nextDeadline - Stopwatch.GetTimestamp();
                if (remainingTicks > 0)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency),
                        ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when Stop / DisposeAsync cancels the token.
            // Cross-cutting Cancellation sub-provider: map _stopReason (local
            // engine vocabulary) to the sub-provider's closed vocabulary:
            // "user" stays as-is, stops driven by an upstream event (capture
            // lost, external Hue interference) are "upstream". Age is computed
            // from the Stopwatch armed at pipeline start.
            string reason = _stopReason switch
            {
                "user"                    => "user",
                "capture_lost"            => "upstream",
                "external"                => "upstream",
                "push_rejected"           => "upstream",
                "frame_processing_failed" => "upstream",
                _                         => "user",
            };
            long ageMs = _startTimestamp != 0
                ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                : -1;
            DeckleCancellationSource.Log.OperationCancelled(
                "ambient-pipeline", reason, (int)ageMs);
        }
        catch (Exception ex)
        {
            // Logged before the abort so a crash during Stopping, which the
            // abort ignores, still reaches the journal.
            DeckleAmbientSource.Log.PushLoopCrashed(ex.GetType().Name, ex.Message);

            // The loop is the only producer of pushes; a dead loop under a
            // Running engine leaves the UI on with frozen lights.
            AbortStartOrStop("push_loop_crashed", static () => { });
        }
    }

    // Tuning with the tick's settings snapshot. isBlack is decided by the
    // caller on the unrounded source (see the black gate in AmbientEngine).
    private SrgbColor TuneColor(SrgbColor color, bool isBlack)
        => AmbientColorPipeline.ApplyTuning(
            color,
            isBlack,
            _saturationBoost,
            _brightnessCurveX1,
            _brightnessCurveY1,
            _brightnessCurveX2,
            _brightnessCurveY2,
            _minBrightnessEnabled,
            _minBrightness);

    // EMA over the measured tick interval, per key. See AmbientColorSmoother.
    private SrgbColor SmoothColor(string key, SrgbColor tuned)
        => _smoother.Smooth(key, tuned, _smoothingAlpha, _tickDtSeconds);

    // Publish + notify in one shot for group mode. Always invokes the
    // event — group ticks are atomic, no batching to wait for.
    private void PublishGroupEmitted(LightColor color)
    {
        lock (_emittedLock)
        {
            _emittedColors[GroupKey] = color;
        }
        EmittedColorsChanged?.Invoke();
    }

    // Stash the per-light intent without firing the event — the
    // multi tick fires once at the end of its fan-out so subscribers
    // get a coherent batch update instead of N rapid-fire callbacks.
    private void PublishMultiEmitted(string lightId, LightColor color)
    {
        lock (_emittedLock)
        {
            _emittedColors[lightId] = color;
        }
    }


}

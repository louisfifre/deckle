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
        // The loop runs on the thread-pool ; downstream SetColorAsync /
        // SetLightColorsAsync go through HttpClient which is thread-safe.
        // Any exception on a single tick's push is swallowed as a Warning
        // so a transient bridge failure (Wi-Fi blip, group renamed mid-
        // session) does not kill the loop — the next tick retries.
        try
        {
            while (!ct.IsCancellationRequested)
            {
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
                    // Sampler hasn't produced a frame yet (first ~66 ms
                    // after Start). Wait one cadence and retry.
                    await Task.Delay(_pushIntervalMs, ct).ConfigureAwait(false);
                    continue;
                }

                bool pushDetailEnabled = OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleAmbientSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Push);
                bool heartbeatDetailEnabled = OperationalLogAdmission.IsDetailEnabled(
                    OperationalLogActivity.Ambient,
                    DeckleAmbientSource.Log,
                    EventLevel.Verbose,
                    (EventKeywords)Keywords.Heartbeat);

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
                else if (_hbTicks != 0 || _hbPushed != 0 || _hbDropped != 0
                      || _hbUnmappedLights != 0 || _hbPushDurationsMs is { Count: > 0 })
                {
                    ResetHeartbeatWindow();
                }

                await Task.Delay(GetPushDelayMs(), ct).ConfigureAwait(false);
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
                "user"         => "user",
                "capture_lost" => "upstream",
                "external"     => "upstream",
                _              => "user",
            };
            long ageMs = _startTimestamp != 0
                ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000 / Stopwatch.Frequency
                : -1;
            DeckleCancellationSource.Log.OperationCancelled(
                "ambient-pipeline", reason, (int)ageMs);
        }
        catch (Exception ex)
        {
            DeckleAmbientSource.Log.PushLoopCrashed();
            DeckleAmbientSource.Log.PushLoopCrashedDetail(ex.GetType().Name, ex.Message);
        }
    }

    private (byte R, byte G, byte B, bool IsDark) PreparePushColor(byte r, byte g, byte b)
    {
        bool isDark = r <= OffThreshold && g <= OffThreshold && b <= OffThreshold;
        var tuned = AmbientColorPipeline.ApplyTuning(
            isDark ? (byte)0 : r,
            isDark ? (byte)0 : g,
            isDark ? (byte)0 : b,
            isDark,
            _saturationBoost,
            _brightnessCurveX1,
            _brightnessCurveY1,
            _brightnessCurveX2,
            _brightnessCurveY2,
            _minBrightnessEnabled,
            _minBrightness);
        return (tuned.R, tuned.G, tuned.B, isDark);
    }

    // EMA smoothing — group mode. State carried in _smoothedR/G/B as
    // float so a slow ramp progresses each tick instead of being
    // clipped to the previous integer step. Alpha is read from the
    // tick-time snapshot _smoothingAlpha (refreshed at the top of
    // PushLoopAsync). On first call (sentinel -1f) and on alpha ≥ 1
    // the filter passes through without fading from black.
    private (byte R, byte G, byte B) ApplyGroupSmoothing(byte r, byte g, byte b)
    {
        float alpha = (float)_smoothingAlpha;
        if (_smoothedR < 0f || alpha >= 1f)
        {
            _smoothedR = r;
            _smoothedG = g;
            _smoothedB = b;
        }
        else
        {
            _smoothedR = alpha * r + (1f - alpha) * _smoothedR;
            _smoothedG = alpha * g + (1f - alpha) * _smoothedG;
            _smoothedB = alpha * b + (1f - alpha) * _smoothedB;
        }
        return (
            (byte)Math.Clamp((int)MathF.Round(_smoothedR), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(_smoothedG), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(_smoothedB), 0, 255));
    }

    // EMA smoothing — multi-light mode. One EMA trail per fixture id ;
    // a new id seen for the first time adopts its raw value (no
    // fade-in from black). Same semantics as ApplyGroupSmoothing
    // otherwise.
    private (byte R, byte G, byte B) ApplyMultiSmoothing(string lightId, byte r, byte g, byte b)
    {
        float alpha = (float)_smoothingAlpha;
        (float R, float G, float B) state;
        bool seeded = _multiSmoothed.TryGetValue(lightId, out state);
        if (!seeded || alpha >= 1f)
        {
            state = (r, g, b);
        }
        else
        {
            state = (
                alpha * r + (1f - alpha) * state.R,
                alpha * g + (1f - alpha) * state.G,
                alpha * b + (1f - alpha) * state.B);
        }
        _multiSmoothed[lightId] = state;
        return (
            (byte)Math.Clamp((int)MathF.Round(state.R), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(state.G), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(state.B), 0, 255));
    }

    // Publish + notify in one shot for group mode. Always invokes the
    // event — group ticks are atomic, no batching to wait for.
    private void PublishGroupEmitted(byte r, byte g, byte b)
    {
        lock (_emittedLock)
        {
            _emittedColors["group"] = new LightColor(r, g, b);
        }
        EmittedColorsChanged?.Invoke();
    }

    // Stash the per-light intent without firing the event — the
    // multi tick fires once at the end of its fan-out so subscribers
    // get a coherent batch update instead of N rapid-fire callbacks.
    private void PublishMultiEmitted(string lightId, byte r, byte g, byte b)
    {
        lock (_emittedLock)
        {
            _emittedColors[lightId] = new LightColor(r, g, b);
        }
    }


}

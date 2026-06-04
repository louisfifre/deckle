using System.Diagnostics;
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
                _saturationBoost                = ambient.SaturationBoost;
                _brightnessCurveType            = ambient.BrightnessCurveType;
                _brightnessCurveParam           = ambient.BrightnessCurveParam;
                _brightnessCurveSCurveSteepness = ambient.BrightnessCurveSCurveSteepness;
                _minBrightness                  = ambient.MinBrightness;
                _changeThreshold                = ambient.ChangeThreshold;
                _smoothingAlpha                 = ambient.SmoothingAlpha;
                _borderMode                     = ambient.BorderMode;
                _borderDepth                    = ambient.BorderDepth;
                _borderCells                    = ambient.BorderCells;

                var sample = _sampler!.LatestSample;
                if (sample is null)
                {
                    // Sampler hasn't produced a frame yet (first ~66 ms
                    // after Start). Wait one cadence and retry.
                    await Task.Delay(_pushIntervalMs, ct).ConfigureAwait(false);
                    continue;
                }

                if (_multiLightActive)
                {
                    await MultiLightTickAsync(sample, ct).ConfigureAwait(false);
                }
                else
                {
                    await GroupTickAsync(sample, ct).ConfigureAwait(false);
                }

                _hbTicks++;
                MaybeEmitHeartbeat();

                await Task.Delay(_pushIntervalMs, ct).ConfigureAwait(false);
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
            DeckleAmbientSource.Log.PushLoopCrashed(ex.GetType().Name, ex.Message);
        }
    }

    private async Task GroupTickAsync(SampledFrame sample, CancellationToken ct)
    {
        var avg = sample.Average;

        // Clamp near-black to true black so the lights turn off
        // instead of glowing faintly. See OffThreshold rationale.
        bool isDark = avg.R <= OffThreshold
                   && avg.G <= OffThreshold
                   && avg.B <= OffThreshold;
        byte rawR = isDark ? (byte)0 : avg.R;
        byte rawG = isDark ? (byte)0 : avg.G;
        byte rawB = isDark ? (byte)0 : avg.B;

        // Apply HDR tuning (saturation boost + min brightness floor)
        // BEFORE the early-exit so a user moving the AmbientPage
        // slider on a static screen still gets the new look pushed —
        // comparing on the raw values would suppress the change.
        var tuned = ApplyTuning(rawR, rawG, rawB, isDark);
        byte targetR = tuned.R;
        byte targetG = tuned.G;
        byte targetB = tuned.B;

        // Temporal smoothing on the tuned colour. See _smoothedR/G/B
        // field doc — damps small per-frame jitter (moving highlights
        // in a globally dark scene) without dulling real cuts. Applied
        // before the delta gate so the gate compares the eye-relevant
        // colour, not the raw sampler output.
        (targetR, targetG, targetB) = ApplyGroupSmoothing(targetR, targetG, targetB);

        // Publish the intent colour for the Playground swatch viewer
        // even when the delta gate drops the actual push.
        PublishGroupEmitted(targetR, targetG, targetB);

        int delta = Math.Abs(targetR - _lastR)
                  + Math.Abs(targetG - _lastG)
                  + Math.Abs(targetB - _lastB);
        bool dropped = _lastR >= 0 && delta < _changeThreshold;

        if (dropped)
        {
            _droppedCount++;
            _hbDropped++;
            return; // Silent : the heartbeat will summarise.
        }

        var color = new LightColor(targetR, targetG, targetB);
        try
        {
            long httpStart = Stopwatch.GetTimestamp();
            await _output!.SetColorAsync(color, ct).ConfigureAwait(false);
            double httpMs = (Stopwatch.GetTimestamp() - httpStart) * 1000.0 / Stopwatch.Frequency;
            _hbHttpDurationsMs.Add(httpMs);

            _lastR = targetR; _lastG = targetG; _lastB = targetB;
            // Stamp the pushed Hue state for echo discrimination — the
            // bridge emits a grouped_light EventStream update for this
            // PUT, sometimes later than a pure timing window can cover.
            if (_managedGroupId is not null)
                RecordHuePush("group:" + _managedGroupId, color, DateTimeOffset.UtcNow);
            _pushedCount++;
            _hbPushed++;
            // Verbose gating is handled by the LogWindow drop filter
            // (App.OnLaunched) : provider=Deckle.Ambient + capture
            // gate open + user toggle off ⇒ this Verbose is filtered
            // before buffer insertion. No call-site check needed.
            DeckleAmbientSource.Log.PushGroup(targetR, targetG, targetB, isDark, httpMs);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Warning unconditional — capture-activity gating never
            // suppresses faults, the user needs to see when the bridge
            // throws even with the toggle off.
            DeckleAmbientSource.Log.PushGroupFailed(ex.GetType().Name, ex.Message);
        }
    }

    private async Task MultiLightTickAsync(SampledFrame sample, CancellationToken ct)
    {
        if (_multiLights is null || _multiLights.Count == 0 || _multiLastPushed is null)
            return;

        // Snapshot the per-light state from the host once per tick so
        // we re-read the live dictionary at most once even if a slider
        // mutation lands between fan-out steps.
        var zoneAssignments = _host.Ambient.LightZones;
        var lightBrightness = _host.Ambient.LightBrightness;

        // Sample the four border zones once per tick — cheap (each
        // averages ~50-100 cells of a 30×17 grid) and one set of
        // numbers shared across all lights that map to the same zone.
        // Zones with no assigned light are still computed for the
        // overlay UI but their result isn't pushed anywhere.
        // Resolve the band thickness in cells for each axis. The top /
        // bottom bands slice the rows axis, the left / right bands slice
        // the cols axis ; in Share mode the same fraction yields fewer
        // rows than cols on a 16:9 grid, in Cells mode the same count
        // applies on every edge.
        int bandRows = AmbientZoneSampler.ResolveBandCells(_borderMode, _borderDepth, _borderCells, sample.Rows);
        int bandCols = AmbientZoneSampler.ResolveBandCells(_borderMode, _borderDepth, _borderCells, sample.Cols);

        var topColor    = AmbientZoneSampler.SampleZone(sample, LightZone.Top,    bandRows);
        var bottomColor = AmbientZoneSampler.SampleZone(sample, LightZone.Bottom, bandRows);
        var leftColor   = AmbientZoneSampler.SampleZone(sample, LightZone.Left,   bandCols);
        var rightColor  = AmbientZoneSampler.SampleZone(sample, LightZone.Right,  bandCols);

        // Per-light fan-out + per-light early-exit. We build a
        // dictionary of (lightId → colour) only for lights whose target
        // colour has changed enough to warrant a push ; lights mapped
        // to <see cref="LightZone.None"/> (or unmapped entirely) are
        // skipped without counting as dropped — they're explicit
        // opt-outs, not throttled pushes.
        var toPush = new Dictionary<string, LightColor>(_multiLights.Count);
        int droppedThisTick = 0;
        int unmappedThisTick = 0;

        foreach (var light in _multiLights)
        {
            LightZone zone = (zoneAssignments is not null && zoneAssignments.TryGetValue(light.Id, out var z))
                ? z
                : LightZone.None;

            if (zone == LightZone.None)
            {
                unmappedThisTick++;
                continue;
            }

            LightColor zoneColor = zone switch
            {
                LightZone.Top    => topColor,
                LightZone.Bottom => bottomColor,
                LightZone.Left   => leftColor,
                LightZone.Right  => rightColor,
                _                => LightColor.Black,
            };

            // Apply the per-light brightness multiplier in [0, 1].
            // Scaling RGB linearly here halves Hue's derived `bri`
            // (max-channel based, see HueColorMath) so the lamp shows
            // the same chromaticity at the requested intensity. The
            // multiplier defaults to 1.0 when the user hasn't touched
            // the slider yet.
            double bri = 1.0;
            if (lightBrightness is not null && lightBrightness.TryGetValue(light.Id, out var b))
                bri = Math.Clamp(b, 0.0, 1.0);
            byte scaledR = (byte)Math.Round(zoneColor.R * bri);
            byte scaledG = (byte)Math.Round(zoneColor.G * bri);
            byte scaledB = (byte)Math.Round(zoneColor.B * bri);

            // Off-threshold applied per light independently after the
            // brightness scale — a zone of the screen can be near-black
            // while the rest is bright, AND the user can pin a single
            // lamp to "off" by sliding its brightness to 0 (which
            // collapses scaledR/G/B below the threshold).
            bool isDark = scaledR <= OffThreshold
                       && scaledG <= OffThreshold
                       && scaledB <= OffThreshold;
            byte rawR = isDark ? (byte)0 : scaledR;
            byte rawG = isDark ? (byte)0 : scaledG;
            byte rawB = isDark ? (byte)0 : scaledB;

            // Apply HDR tuning (saturation boost + min brightness)
            // per light, same rationale as GroupTick : the early-exit
            // compares on tuned values so a slider move always pushes.
            var tuned = ApplyTuning(rawR, rawG, rawB, isDark);
            byte targetR = tuned.R;
            byte targetG = tuned.G;
            byte targetB = tuned.B;

            // Per-light temporal smoothing on the tuned colour. State
            // is keyed by fixture id so each lamp keeps its own EMA
            // trail (a fast cut on the left side doesn't reset the
            // right-side lamp's history).
            (targetR, targetG, targetB) = ApplyMultiSmoothing(light.Id, targetR, targetG, targetB);

            // Stash the intent colour for the Playground swatches —
            // batched event fires once at the end of the loop.
            PublishMultiEmitted(light.Id, targetR, targetG, targetB);

            var prev = _multiLastPushed.TryGetValue(light.Id, out var last) ? last : (-1, -1, -1);
            int delta = Math.Abs(targetR - prev.Item1)
                      + Math.Abs(targetG - prev.Item2)
                      + Math.Abs(targetB - prev.Item3);
            bool dropped = prev.Item1 >= 0 && delta < _changeThreshold;

            if (dropped)
            {
                droppedThisTick++;
                continue;
            }

            toPush[light.Id] = new LightColor(targetR, targetG, targetB);
            _multiLastPushed[light.Id] = (targetR, targetG, targetB);
        }

        // Track per-tick lights-with-no-zone count so the heartbeat
        // surfaces the user's "lights assigned to None" backlog
        // without us logging it every tick.
        _hbUnmappedLights += unmappedThisTick;

        // Fire the observable event once per tick. Even when toPush is
        // empty (every light dropped by the delta gate) the intent map
        // has been refreshed by PublishMultiEmitted ; the Playground
        // swatches want to reflect that.
        EmittedColorsChanged?.Invoke();

        if (toPush.Count == 0)
        {
            _droppedCount++;
            _hbDropped++;
            return; // Silent : the heartbeat will summarise.
        }

        try
        {
            var multi = (IMultiLightOutput)_output!;
            long httpStart = Stopwatch.GetTimestamp();
            await multi.SetLightColorsAsync(toPush, ct).ConfigureAwait(false);
            double httpMs = (Stopwatch.GetTimestamp() - httpStart) * 1000.0 / Stopwatch.Frequency;
            _hbHttpDurationsMs.Add(httpMs);

            // Stamp pushed Hue states for echo discrimination — the
            // bridge emits a light EventStream update for each PUT,
            // sometimes later than a pure timing window can cover.
            var nowUtc = DateTimeOffset.UtcNow;
            foreach (var (id, pushedColor) in toPush)
            {
                RecordHuePush("light:" + id, pushedColor, nowUtc);
            }
            _pushedCount++;
            _hbPushed++;
            DeckleAmbientSource.Log.PushMulti(toPush.Count, _multiLights.Count, FormatPushedColors(toPush), httpMs);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            DeckleAmbientSource.Log.PushMultiFailed(ex.GetType().Name, ex.Message);
        }
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

    private (byte R, byte G, byte B) ApplyTuning(byte r, byte g, byte b, bool isDark)
        => AmbientColorPipeline.ApplyTuning(
            r,
            g,
            b,
            isDark,
            _saturationBoost,
            _brightnessCurveType,
            _brightnessCurveParam,
            _brightnessCurveSCurveSteepness,
            _minBrightness);

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

    private void MaybeEmitHeartbeat()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - _hbTimestamp) * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs < HeartbeatIntervalMs) return;

        // HTTP stats over the elapsed window. Skipped from the line
        // when no push happened in the window (static screen) — the
        // ticks=N pushed=0 prefix already says "loop alive, nothing
        // to push", a "http_avg_ms=0.0" suffix would be misleading.
        string httpStats = "";
        if (_hbHttpDurationsMs.Count > 0)
        {
            double min = double.MaxValue, max = 0, sum = 0;
            foreach (var v in _hbHttpDurationsMs)
            {
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
            }
            double avg = sum / _hbHttpDurationsMs.Count;
            var sorted = _hbHttpDurationsMs.ToArray();
            Array.Sort(sorted);
            int p95Idx = Math.Max(0, Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1));
            double p95 = sorted[p95Idx];
            httpStats = $" | http_avg_ms={avg:F1} | http_p95_ms={p95:F1} | http_max_ms={max:F1}";
        }

        // Per-tick Verbose : filtered by the LogWindow drop filter
        // (capture gate + user toggle). Counters are reset whether
        // the line was emitted or not, so the next heartbeat window
        // starts from zero — the metric stays correct when the
        // toggle flips mid-session.
        DeckleAmbientSource.Log.Heartbeat(
            _multiLightActive ? "multi" : "group",
            elapsedMs / 1000.0,
            _hbTicks,
            _hbPushed,
            _hbDropped,
            _multiLightActive ? _hbUnmappedLights : 0,
            httpStats);

        _hbTimestamp = now;
        _hbTicks = _hbPushed = _hbDropped = _hbUnmappedLights = 0;
        _hbHttpDurationsMs.Clear();
    }
}

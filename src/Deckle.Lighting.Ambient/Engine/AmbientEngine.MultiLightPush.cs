using System.Diagnostics;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    private async Task MultiLightTickAsync(
        SampledFrame sample,
        CancellationToken ct,
        bool pushDetailEnabled,
        bool heartbeatDetailEnabled)
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
            // Apply HDR tuning (saturation boost + min brightness)
            // per light, same rationale as GroupTick : the early-exit
            // compares on tuned values so a slider move always pushes.
            var tuned = PreparePushColor(scaledR, scaledG, scaledB);
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
            var targetColor = new LightColor(targetR, targetG, targetB);
            bool dropped = AmbientPushGate.ShouldDrop(
                targetColor,
                prev,
                _changeThreshold,
                _requiresContinuousColorUpdates);

            if (dropped)
            {
                droppedThisTick++;
                continue;
            }

            toPush[light.Id] = targetColor;
        }

        // Track per-tick lights-with-no-zone count so the heartbeat
        // surfaces the user's "lights assigned to None" backlog
        // without us logging it every tick.
        if (heartbeatDetailEnabled) _hbUnmappedLights += unmappedThisTick;

        // Fire the observable event once per tick. Even when toPush is
        // empty (every light dropped by the delta gate) the intent map
        // has been refreshed by PublishMultiEmitted ; the Playground
        // swatches want to reflect that.
        EmittedColorsChanged?.Invoke();

        if (toPush.Count == 0)
        {
            _droppedCount++;
            if (heartbeatDetailEnabled) _hbDropped++;
            return; // Silent : the heartbeat will summarise.
        }

        try
        {
            var multi = (IMultiLightOutput)_output!;
            bool measurePush = pushDetailEnabled || heartbeatDetailEnabled;
            long pushStart = measurePush ? Stopwatch.GetTimestamp() : 0;
            await multi.SetLightColorsAsync(toPush, ct).ConfigureAwait(false);
            OnPushSucceeded();
            double pushMs = measurePush
                ? (Stopwatch.GetTimestamp() - pushStart) * 1000.0 / Stopwatch.Frequency
                : 0;
            if (heartbeatDetailEnabled)
                (_hbPushDurationsMs ??= new List<double>(128)).Add(pushMs);

            // Stamp pushed Hue states for echo discrimination — the
            // bridge emits a light EventStream update for each PUT,
            // sometimes later than a pure timing window can cover.
            var nowUtc = DateTimeOffset.UtcNow;
            foreach (var (id, pushedColor) in toPush)
            {
                _multiLastPushed[id] = (pushedColor.R, pushedColor.G, pushedColor.B);
                RecordHuePush("light:" + id, pushedColor, nowUtc);
            }
            _pushedCount++;
            if (heartbeatDetailEnabled) _hbPushed++;
            if (pushDetailEnabled)
            {
                DeckleAmbientSource.Log.PushMulti(
                    toPush.Count, _multiLights.Count, FormatPushedColors(toPush), pushMs);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            OnPushFailed(ex);
        }
    }
}

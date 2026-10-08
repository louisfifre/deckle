using System.Diagnostics;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    private async Task GroupTickAsync(
        SampledFrame sample,
        CancellationToken ct,
        bool pushDetailEnabled,
        bool heartbeatDetailEnabled)
    {
        // Black only on an exactly black frame : the gate reads the
        // unrounded linear average, before any tuning.
        var linear = sample.AverageLinear;
        bool isBlack = AmbientColorPipeline.IsBlackSource(linear);

        // Apply HDR tuning (saturation boost + min brightness floor)
        // BEFORE the early-exit so a user moving the AmbientPage
        // slider on a static screen still gets the new look pushed —
        // comparing on the raw values would suppress the change.
        SrgbColor target = TuneColor(SrgbColor.FromLinear(linear), isBlack);

        // Temporal smoothing on the tuned colour — damps small per-frame
        // jitter (moving highlights in a globally dark scene) without
        // dulling real cuts. Applied before the delta gate so the gate
        // compares the eye-relevant colour, not the raw sampler output.
        target = SmoothColor(GroupKey, target);

        // An EMA between different hues can put every channel below the
        // floor even when both endpoints respected it. Re-apply the floor
        // to the final colour that will be compared and pushed.
        if (_minBrightnessEnabled)
            target = AmbientColorPipeline.ApplyMinBrightness(target, _minBrightness);

        // The single quantisation of the tick : everything above stays
        // in float.
        LightColor color = _blackLatch.Quantize(GroupKey, target);

        // Publish the intent colour for the AmbientPreviewCard swatches
        // even when the delta gate drops or holds the actual push.
        PublishGroupEmitted(color);

        var decision = AmbientPushGate.Decide(
            color,
            (_lastR, _lastG, _lastB),
            _changeThreshold,
            _requiresContinuousColorUpdates);

        if (decision == AmbientPushDecision.Drop)
        {
            _droppedCount++;
            if (heartbeatDetailEnabled) _hbDropped++;
            return; // Silent : the heartbeat will summarise.
        }

        // A hold keeps the stream alive with the last pushed colour ; it
        // counts as a push.
        if (decision == AmbientPushDecision.HoldPrevious)
            color = AmbientPushGate.PreviousColor((_lastR, _lastG, _lastB));

        try
        {
            bool measurePush = pushDetailEnabled || heartbeatDetailEnabled;
            long pushStart = measurePush ? Stopwatch.GetTimestamp() : 0;
            await _output!.SetColorAsync(color, ct).ConfigureAwait(false);
            OnPushSucceeded();
            double pushMs = measurePush
                ? (Stopwatch.GetTimestamp() - pushStart) * 1000.0 / Stopwatch.Frequency
                : 0;
            if (heartbeatDetailEnabled)
                (_hbPushDurationsMs ??= new List<double>(128)).Add(pushMs);

            _lastR = color.R; _lastG = color.G; _lastB = color.B;
            // Stamp the pushed Hue state for echo discrimination — the
            // bridge emits a grouped_light EventStream update for this
            // PUT, sometimes later than a pure timing window can cover.
            if (_managedGroupId is not null)
                RecordHuePush("group:" + _managedGroupId, color, DateTimeOffset.UtcNow);
            _pushedCount++;
            if (heartbeatDetailEnabled) _hbPushed++;
            if (pushDetailEnabled)
                DeckleAmbientSource.Log.PushGroup(color.R, color.G, color.B, isBlack, pushMs);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            OnPushFailed(ex);
        }
    }
}

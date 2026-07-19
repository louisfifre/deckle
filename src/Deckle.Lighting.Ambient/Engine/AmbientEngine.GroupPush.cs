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
        var avg = sample.Average;

        // Apply HDR tuning (saturation boost + min brightness floor)
        // BEFORE the early-exit so a user moving the AmbientPage
        // slider on a static screen still gets the new look pushed —
        // comparing on the raw values would suppress the change.
        var tuned = PreparePushColor(avg.R, avg.G, avg.B);
        bool isDark = tuned.IsDark;
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

        var color = new LightColor(targetR, targetG, targetB);
        bool dropped = AmbientPushGate.ShouldDrop(
            color,
            (_lastR, _lastG, _lastB),
            _changeThreshold,
            _requiresContinuousColorUpdates);

        if (dropped)
        {
            _droppedCount++;
            if (heartbeatDetailEnabled) _hbDropped++;
            return; // Silent : the heartbeat will summarise.
        }

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

            _lastR = targetR; _lastG = targetG; _lastB = targetB;
            // Stamp the pushed Hue state for echo discrimination — the
            // bridge emits a grouped_light EventStream update for this
            // PUT, sometimes later than a pure timing window can cover.
            if (_managedGroupId is not null)
                RecordHuePush("group:" + _managedGroupId, color, DateTimeOffset.UtcNow);
            _pushedCount++;
            if (heartbeatDetailEnabled) _hbPushed++;
            if (pushDetailEnabled)
                DeckleAmbientSource.Log.PushGroup(targetR, targetG, targetB, isDark, pushMs);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            OnPushFailed(ex);
        }
    }
}

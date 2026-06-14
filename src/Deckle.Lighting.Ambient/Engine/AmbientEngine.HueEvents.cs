// AmbientEngine — Hue EventStream handling (external-change vs. echo
// classification, self-push bookkeeping, event-field formatting).
using System.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    // Called on the EventStream task (HttpClient SSE reader). Decides
    // whether the bridge-side event reflects our own push (echo) or a
    // genuine external command, and if external, stops the engine rather
    // than fighting the user's Hue command. Never blocks — only field
    // reads and a stop request.
    private void OnResourceUpdate(HueResourceUpdate ev)
    {
        // Translate the v2 UUID to the v1 id the engine pushes against.
        // Lights and grouped_lights live in disjoint UUID spaces ;
        // resource.type tells us which map to consult.
        string? v1Id;
        string scopedKey;
        if (ev.ResourceType == "grouped_light")
        {
            if (_v2GroupedLightMap is null || !_v2GroupedLightMap.TryGetValue(ev.V2ResourceId, out v1Id))
                return;
            // Only react for the group the engine is currently syncing
            // — other groups on the bridge are not our concern.
            if (_managedGroupId is null || v1Id != _managedGroupId) return;
            // In multi-light mode, group_action events are noise — the
            // engine doesn't push the group, only individual lights.
            if (_multiLightActive) return;
            scopedKey = "group:" + v1Id;
        }
        else if (ev.ResourceType == "light")
        {
            if (_v2LightMap is null || !_v2LightMap.TryGetValue(ev.V2ResourceId, out v1Id)) return;
            // In group mode we don't drive per-light, so per-light
            // events shouldn't trigger a group stop.
            if (!_multiLightActive) return;
            if (_multiLights is null || !_multiLights.Any(l => l.Id == v1Id)) return;
            scopedKey = "light:" + v1Id;
        }
        else
        {
            return;
        }

        AmbientHuePushedState? lastPushed = null;
        lock (_hueEchoLock)
        {
            if (_lastHuePushes.TryGetValue(scopedKey, out var pushed))
            {
                lastPushed = pushed;
            }
        }

        var decision = AmbientHueEchoClassifier.Classify(ev, lastPushed, DateTimeOffset.UtcNow);
        if (decision.Kind == AmbientHueEventDecisionKind.Ignore)
        {
            return;
        }

        int ageMs = decision.AgeMs.HasValue
            ? (int)Math.Round(decision.AgeMs.Value)
            : -1;

        if (decision.Kind == AmbientHueEventDecisionKind.Echo)
        {
            DeckleAmbientSource.Log.EchoIgnored(v1Id, ev.ResourceType, ageMs);
            return;
        }

        // Honest stop on external interference : we don't try to wrestle
        // control back. Log and stop the engine off the SSE worker
        // thread (Stop() raises StateChanged, marshalling needs the
        // thread-pool). The user-facing notification for this case
        // belongs to a later error-handling pass — for now the toggle
        // simply flips off and the LogWindow shows the reason.
        AbortStartOrStop(
            "external",
            () =>
            {
                DeckleAmbientSource.Log.ExternalChangeStopped();
                DeckleAmbientSource.Log.ExternalChangeStoppedDetail(
                    v1Id,
                    ev.ResourceType,
                    ageMs,
                    FormatHueEventOn(ev.On),
                    FormatHueEventBrightness(ev.Brightness),
                    FormatHueEventXy(ev.Xy));
            });
    }

    private void RecordHuePush(string scopedKey, LightColor color, DateTimeOffset pushedAt)
    {
        var pushed = new AmbientHuePushedState(pushedAt, HueStateProjection.FromLightColor(color));
        lock (_hueEchoLock)
        {
            _lastHuePushes[scopedKey] = pushed;
        }
    }

    private void ClearLastHuePushes()
    {
        lock (_hueEchoLock)
        {
            _lastHuePushes.Clear();
        }
    }

    private static string FormatHueEventOn(bool? on)
        => on.HasValue ? (on.Value ? "true" : "false") : "null";

    private static string FormatHueEventBrightness(int? brightness)
        => brightness.HasValue ? brightness.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";

    private static string FormatHueEventXy((float X, float Y)? xy)
        => xy.HasValue
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{xy.Value.X:F4},{xy.Value.Y:F4}")
            : "null";
}

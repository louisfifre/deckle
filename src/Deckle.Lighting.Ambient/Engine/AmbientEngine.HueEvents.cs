// AmbientEngine — Hue EventStream handling (bridge-change attribution,
// self-push bookkeeping, event-field formatting).
using System.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Lighting;
using Deckle.Vision;

namespace Deckle.Lighting.Ambient;

public sealed partial class AmbientEngine
{
    // Called on the EventStream task (HttpClient SSE reader). Attributes
    // a bridge-side event to either our own pending push (echo) or a stable
    // bridge-side change Ambient should not fight. Never blocks — only
    // field reads and a stop request.
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

        AmbientHueAttributionState? attributionState = null;
        lock (_hueAttributionLock)
        {
            if (_hueAttributionStates.TryGetValue(scopedKey, out var state))
            {
                attributionState = state;
            }
        }

        var decision = AmbientHueChangeAttributor.Classify(ev, attributionState, DateTimeOffset.UtcNow);
        if (decision.Kind == AmbientHueChangeDecisionKind.Ignore)
        {
            return;
        }

        int ageMs = decision.AgeMs.HasValue
            ? (int)Math.Round(decision.AgeMs.Value)
            : -1;

        if (decision.Kind == AmbientHueChangeDecisionKind.Echo)
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
                DeckleAmbientSource.Log.ExternalChangeDecisionDetail(
                    v1Id,
                    ev.ResourceType,
                    ageMs,
                    FormatHueEventOn(ev.On),
                    FormatPushedOn(attributionState),
                    FormatHueEventBrightness(ev.Brightness),
                    FormatPushedBrightness(attributionState),
                    FormatHueEventXy(ev.Xy),
                    FormatPushedXy(attributionState),
                    FormatXyDelta(ev.Xy, attributionState),
                    decision.Basis);
            });
    }

    private void RecordHuePush(string scopedKey, LightColor color, DateTimeOffset pushedAt)
    {
        var pushed = new AmbientHueAttributionState(pushedAt, HueStateProjection.FromLightColor(color));
        lock (_hueAttributionLock)
        {
            _hueAttributionStates[scopedKey] = pushed;
        }
    }

    private void ClearHueAttributionStates()
    {
        lock (_hueAttributionLock)
        {
            _hueAttributionStates.Clear();
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

    private static string FormatPushedOn(AmbientHueAttributionState? pushed)
        => pushed.HasValue ? (pushed.Value.DesiredState.On ? "true" : "false") : "none";

    private static string FormatPushedBrightness(AmbientHueAttributionState? pushed)
        => pushed.HasValue
            ? (pushed.Value.DesiredState.Brightness.HasValue
                ? pushed.Value.DesiredState.Brightness.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "null")
            : "none";

    private static string FormatPushedXy(AmbientHueAttributionState? pushed)
        => pushed.HasValue
            ? (pushed.Value.DesiredState.Xy.HasValue
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{pushed.Value.DesiredState.Xy.Value.X:F4},{pushed.Value.DesiredState.Xy.Value.Y:F4}")
                : "null")
            : "none";

    private static string FormatXyDelta((float X, float Y)? eventXy, AmbientHueAttributionState? pushed)
    {
        if (!eventXy.HasValue || !pushed.HasValue || !pushed.Value.DesiredState.Xy.HasValue)
        {
            return "null";
        }

        var pushedXy = pushed.Value.DesiredState.Xy.Value;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Math.Abs(eventXy.Value.X - pushedXy.X):F4},{Math.Abs(eventXy.Value.Y - pushedXy.Y):F4}");
    }
}

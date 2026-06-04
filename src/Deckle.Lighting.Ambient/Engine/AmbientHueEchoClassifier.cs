using Deckle.Lighting.Hue;

namespace Deckle.Lighting.Ambient;

internal enum AmbientHueEventDecisionKind
{
    Ignore,
    Echo,
    External,
}

internal readonly record struct AmbientHuePushedState(
    DateTimeOffset PushedAt,
    HueProjectedState State);

internal readonly record struct AmbientHueEventDecision(
    AmbientHueEventDecisionKind Kind,
    double? AgeMs);

internal static class AmbientHueEchoClassifier
{
    private const int BrightnessTolerance = 1;
    private const float XyTolerance = 0.002f;

    public static AmbientHueEventDecision Classify(
        HueResourceUpdate update,
        AmbientHuePushedState? lastPushed,
        DateTimeOffset nowUtc)
    {
        if (!HasStatePayload(update))
        {
            return new AmbientHueEventDecision(AmbientHueEventDecisionKind.Ignore, null);
        }

        if (lastPushed is { } pushed)
        {
            // State match is authoritative — the age is telemetry only. The
            // last push is our standing intent for this light until we push
            // again ; the per-light delta-gate suspends pushes precisely while
            // the colour is unchanged, so a stale slot still reflects what
            // ambient wants on screen. If the bridge reports that same state
            // back it is our own (possibly delayed) echo, whatever its age ; a
            // genuine external command differs on at least one carried
            // component → Matches fails → External. The old fixed 2 s window
            // only manufactured false external-stops on static zones (see
            // AmbientHueEchoClassifierTests, incident 2026-06-04).
            double ageMs = (nowUtc - pushed.PushedAt).Duration().TotalMilliseconds;
            if (Matches(update, pushed.State))
            {
                return new AmbientHueEventDecision(AmbientHueEventDecisionKind.Echo, ageMs);
            }

            return new AmbientHueEventDecision(AmbientHueEventDecisionKind.External, ageMs);
        }

        return new AmbientHueEventDecision(AmbientHueEventDecisionKind.External, null);
    }

    private static bool HasStatePayload(HueResourceUpdate update)
        => update.On.HasValue || update.Brightness.HasValue || update.Xy.HasValue;

    private static bool Matches(HueResourceUpdate update, HueProjectedState pushed)
    {
        if (update.On.HasValue && update.On.Value != pushed.On)
        {
            return false;
        }

        if (update.On == false && !pushed.On)
        {
            return true;
        }

        if (update.Brightness.HasValue)
        {
            if (!pushed.Brightness.HasValue)
            {
                return false;
            }

            if (Math.Abs(update.Brightness.Value - pushed.Brightness.Value) > BrightnessTolerance)
            {
                return false;
            }
        }

        if (update.Xy.HasValue)
        {
            if (!pushed.Xy.HasValue)
            {
                return false;
            }

            var eventXy = update.Xy.Value;
            var pushedXy = pushed.Xy.Value;
            if (Math.Abs(eventXy.X - pushedXy.X) > XyTolerance ||
                Math.Abs(eventXy.Y - pushedXy.Y) > XyTolerance)
            {
                return false;
            }
        }

        return true;
    }
}

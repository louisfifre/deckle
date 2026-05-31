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
    public static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(2);

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
            var age = nowUtc - pushed.PushedAt;
            double ageMs = age.Duration().TotalMilliseconds;
            if (age.Duration() <= EchoWindow && Matches(update, pushed.State))
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

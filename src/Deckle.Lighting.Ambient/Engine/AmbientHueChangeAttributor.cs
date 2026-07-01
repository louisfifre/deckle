using Deckle.Lighting;

namespace Deckle.Lighting.Ambient;

internal enum AmbientHueChangeDecisionKind
{
    Ignore,
    Echo,
    External,
}

internal readonly record struct AmbientHueAttributionState(
    DateTimeOffset LastPushedAt,
    HueProjectedState DesiredState);

internal readonly record struct AmbientHueChangeDecision(
    AmbientHueChangeDecisionKind Kind,
    double? AgeMs,
    string Basis);

internal static class AmbientHueChangeAttributor
{
    internal static TimeSpan PendingEchoWindow { get; } = TimeSpan.FromSeconds(10);

    private const int BrightnessTolerance = 1;
    private const float XyTolerance = 0.002f;

    public static AmbientHueChangeDecision Classify(
        HueResourceUpdate update,
        AmbientHueAttributionState? state,
        DateTimeOffset nowUtc)
    {
        if (!HasStatePayload(update))
        {
            return new AmbientHueChangeDecision(AmbientHueChangeDecisionKind.Ignore, null, "no_state");
        }

        if (state is not { } current)
        {
            return new AmbientHueChangeDecision(AmbientHueChangeDecisionKind.Ignore, null, "no_baseline");
        }

        TimeSpan age = (nowUtc - current.LastPushedAt).Duration();
        double ageMs = age.TotalMilliseconds;

        // While a push is still settling, CLIP v2 events are attribution
        // evidence, not provenance. Hue often echoes our own PUT as partial
        // xy-only updates after gamut normalization, so xy alone is weak here.
        // Strong fields still win: an on/off or brightness divergence means
        // somebody else is taking over and Ambient should stop.
        if (age <= PendingEchoWindow)
        {
            if (HasStrongMismatch(update, current.DesiredState))
            {
                return new AmbientHueChangeDecision(
                    AmbientHueChangeDecisionKind.External,
                    ageMs,
                    "pending_strong_" + FormatMismatch(update, current.DesiredState));
            }

            return new AmbientHueChangeDecision(
                AmbientHueChangeDecisionKind.Echo,
                ageMs,
                Matches(update, current.DesiredState) ? "pending_match" : "pending_soft");
        }

        if (Matches(update, current.DesiredState))
        {
            return new AmbientHueChangeDecision(AmbientHueChangeDecisionKind.Echo, ageMs, "stable_match");
        }

        return new AmbientHueChangeDecision(
            AmbientHueChangeDecisionKind.External,
            ageMs,
            "stable_" + FormatMismatch(update, current.DesiredState));
    }

    private static bool HasStatePayload(HueResourceUpdate update)
        => update.On.HasValue || update.Brightness.HasValue || update.Xy.HasValue;

    private static bool HasStrongMismatch(HueResourceUpdate update, HueProjectedState desired)
    {
        if (update.On.HasValue && update.On.Value != desired.On)
        {
            return true;
        }

        if (!desired.On && (update.Brightness.HasValue || update.Xy.HasValue))
        {
            return true;
        }

        if (update.Brightness.HasValue)
        {
            if (!desired.Brightness.HasValue)
            {
                return true;
            }

            if (Math.Abs(update.Brightness.Value - desired.Brightness.Value) > BrightnessTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(HueResourceUpdate update, HueProjectedState desired)
    {
        if (update.On.HasValue && update.On.Value != desired.On)
        {
            return false;
        }

        if (update.On == false && !desired.On)
        {
            return true;
        }

        if (update.Brightness.HasValue)
        {
            if (!desired.Brightness.HasValue)
            {
                return false;
            }

            if (Math.Abs(update.Brightness.Value - desired.Brightness.Value) > BrightnessTolerance)
            {
                return false;
            }
        }

        if (update.Xy.HasValue)
        {
            if (!desired.Xy.HasValue)
            {
                return false;
            }

            var eventXy = update.Xy.Value;
            var desiredXy = desired.Xy.Value;
            if (Math.Abs(eventXy.X - desiredXy.X) > XyTolerance ||
                Math.Abs(eventXy.Y - desiredXy.Y) > XyTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatMismatch(HueResourceUpdate update, HueProjectedState desired)
    {
        var fields = new List<string>(3);

        if (update.On.HasValue && update.On.Value != desired.On)
        {
            fields.Add("on");
        }

        if (update.Brightness.HasValue)
        {
            if (!desired.Brightness.HasValue)
            {
                fields.Add("bri_missing");
            }
            else
            {
                int delta = Math.Abs(update.Brightness.Value - desired.Brightness.Value);
                if (delta > BrightnessTolerance)
                {
                    fields.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"bri:{delta}"));
                }
            }
        }

        if (update.Xy.HasValue)
        {
            if (!desired.Xy.HasValue)
            {
                fields.Add("xy_missing");
            }
            else
            {
                var eventXy = update.Xy.Value;
                var desiredXy = desired.Xy.Value;
                if (Math.Abs(eventXy.X - desiredXy.X) > XyTolerance ||
                    Math.Abs(eventXy.Y - desiredXy.Y) > XyTolerance)
                {
                    fields.Add("xy");
                }
            }
        }

        return fields.Count == 0 ? "unknown" : string.Join(",", fields);
    }
}

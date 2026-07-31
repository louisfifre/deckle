namespace Deckle.Lighting.Ambient;

internal static class AmbientPushCadence
{
    private const int SmoothingReferenceRateHz = 15;
    private const int RestGroupRateHz = 15;
    private const int RestMultiLightRateHz = 10;

    public static int ResolveRateHz(int? preferredTransportRateHz, bool multiLight)
        => preferredTransportRateHz is > 0
            ? preferredTransportRateHz.Value
            : multiLight ? RestMultiLightRateHz : RestGroupRateHz;

    public static double AdaptSmoothingAlpha(double referenceAlpha, int actualRateHz)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actualRateHz);

        double clampedAlpha = Math.Clamp(referenceAlpha, 0.0, 1.0);
        if (clampedAlpha is 0.0 or 1.0)
            return clampedAlpha;

        return 1.0 - Math.Pow(
            1.0 - clampedAlpha,
            (double)SmoothingReferenceRateHz / actualRateHz);
    }

    public static long AdvanceDeadline(
        long previousDeadline,
        long tickStartedAt,
        long tickCompletedAt,
        long intervalTicks,
        out long skippedSlots)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalTicks);

        // A late continuation still delivers one latest-colour tick, but the
        // next nominal slot must not follow it as a catch-up burst. Re-anchor
        // the schedule on the tick that actually ran, then skip only complete
        // intervals missed before or during that tick.
        skippedSlots = Math.Max(0, (tickStartedAt - previousDeadline) / intervalTicks);

        long nextDeadline = tickStartedAt + intervalTicks;
        if (tickCompletedAt >= nextDeadline)
        {
            long intervalsMissedDuringTick =
                ((tickCompletedAt - nextDeadline) / intervalTicks) + 1;
            skippedSlots += intervalsMissedDuringTick;
            nextDeadline += intervalsMissedDuringTick * intervalTicks;
        }

        return nextDeadline;
    }
}

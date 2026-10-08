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
        return AdaptSmoothingAlpha(referenceAlpha, 1.0 / actualRateHz);
    }

    // The stored alpha is the EMA step for one 1/15 s tick. Over dtSeconds
    // the same response keeps (1 - alpha)^(dt × 15) of the previous state,
    // so a late or skipped tick catches up instead of slowing the fade.
    // A NaN alpha (a corrupt setting) turns smoothing off rather than
    // poisoning the trail.
    public static double AdaptSmoothingAlpha(double referenceAlpha, double dtSeconds)
    {
        if (double.IsNaN(referenceAlpha))
            return 1.0;

        double clampedAlpha = Math.Clamp(referenceAlpha, 0.0, 1.0);
        if (clampedAlpha is 0.0 or 1.0)
            return clampedAlpha;
        if (!(dtSeconds > 0.0))
            return 0.0;

        return 1.0 - Math.Pow(
            1.0 - clampedAlpha,
            dtSeconds * SmoothingReferenceRateHz);
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

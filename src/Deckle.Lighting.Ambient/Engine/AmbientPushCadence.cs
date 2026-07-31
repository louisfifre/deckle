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
        long now,
        long intervalTicks,
        out long skippedSlots)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalTicks);

        long nextDeadline = previousDeadline + intervalTicks;
        if (now <= nextDeadline)
        {
            skippedSlots = 0;
            return nextDeadline;
        }

        skippedSlots = ((now - nextDeadline) / intervalTicks) + 1;
        return nextDeadline + (skippedSlots * intervalTicks);
    }
}

namespace Deckle.Lighting;

/// <summary>
/// A light's CLIP v1 state, read before Ambient takes the light over so it
/// can be written back when Ambient lets go. <see cref="ColorMode"/> says
/// which colour fields the light is following ("xy", "ct" or "hs"); a
/// white-only bulb has none and only carries on and brightness.
/// </summary>
public sealed record HueLightState(
    bool On,
    byte Brightness,
    string? ColorMode,
    double[]? Xy,
    int? ColorTemperature,
    int? Hue,
    byte? Saturation)
{
    // The bridge echoes a written state back rounded (xy to four decimals,
    // hue in steps of its own), so a state read back counts as the same one
    // within these margins rather than to the last bit.
    private const int    BrightnessTolerance  = 2;
    private const double XyTolerance          = 0.002;
    private const int    MiredTolerance       = 2;
    private const int    HueTolerance         = 200;
    private const int    SaturationTolerance  = 2;

    /// <summary>
    /// True when <paramref name="actual"/>, read back from the bridge, shows
    /// this state: same on/off, and when on, the same brightness and colour
    /// within the bridge's rounding.
    /// </summary>
    public bool Matches(HueLightState actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (On != actual.On) return false;
        if (!On) return true;
        if (Math.Abs(Brightness - actual.Brightness) > BrightnessTolerance) return false;

        return ColorMode switch
        {
            "xy" when Xy is [var x, var y] =>
                actual.Xy is [var ax, var ay]
                && Math.Abs(x - ax) <= XyTolerance
                && Math.Abs(y - ay) <= XyTolerance,
            "ct" when ColorTemperature is { } ct =>
                actual.ColorTemperature is { } act && Math.Abs(ct - act) <= MiredTolerance,
            "hs" when Hue is { } hue && Saturation is { } sat =>
                actual.Hue is { } ah && actual.Saturation is { } asat
                && Math.Abs(hue - ah) <= HueTolerance
                && Math.Abs(sat - asat) <= SaturationTolerance,
            _ => true,
        };
    }
}

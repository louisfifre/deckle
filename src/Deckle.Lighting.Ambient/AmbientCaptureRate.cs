namespace Deckle.Lighting.Ambient;

/// <summary>
/// The closed set of screen-capture rates Ambient offers, and the rule that
/// maps a stored value onto it. 60 Hz is the default for smoothing and
/// reactivity; 30 and 15 Hz lighten the load on weaker machines. The push
/// to the lights keeps its own transport-bound rate whatever is chosen here.
/// </summary>
public static class AmbientCaptureRate
{
    /// <summary>Rate used when nothing valid is stored.</summary>
    public const int Default = 60;

    /// <summary>Allowed capture rates in Hz, highest first.</summary>
    public static IReadOnlyList<int> Allowed { get; } = [60, 30, 15];

    /// <summary>
    /// Returns the allowed rate nearest to <paramref name="rateHz"/>. A tie
    /// goes to the higher rate (45 Hz gives 60 Hz). Zero or negative values
    /// are not rates and give <see cref="Default"/>.
    /// </summary>
    public static int Snap(int rateHz)
    {
        if (rateHz <= 0) return Default;

        int best = Allowed[0];
        foreach (int candidate in Allowed)
        {
            // Allowed is ordered highest first, so a strict comparison keeps
            // the higher rate on a tie.
            if (Math.Abs(candidate - rateHz) < Math.Abs(best - rateHz))
            {
                best = candidate;
            }
        }
        return best;
    }
}

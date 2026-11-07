namespace Deckle.Lighting.Ambient;

/// <summary>
/// The settings <see cref="AmbientEngine"/> reads only when it starts: the
/// capture monitor, the pipeline shape and the performance mode, which sets
/// the capture rate's ceiling. The app keeps the values a running engine
/// started with and restarts it when one of them changes, so every setting
/// acts without a manual off and on. The Hue bridge fields and the group are
/// also read only at start, but they are not part of this set: changing them
/// does not restart a running engine.
/// </summary>
public readonly record struct AmbientStartSettings(
    string? SelectedMonitorDeviceName,
    bool UseMultiLight,
    bool PerformanceMode)
{
    /// <summary>Reads the start-time values. The performance mode is the
    /// shell's shared signal, not an Ambient setting, so the caller passes it
    /// in.</summary>
    public static AmbientStartSettings From(AmbientSettings settings, bool performanceMode)
        => new(
            settings.SelectedMonitorDeviceName,
            settings.UseMultiLight,
            performanceMode);

    /// <summary>Names of the start-time values that differ from
    /// <paramref name="other"/>, comma-separated; empty when none differs.
    /// The first two are <see cref="AmbientSettings"/> property names; the
    /// third is the shared mode's name.</summary>
    public string ChangedSettings(AmbientStartSettings other)
    {
        var changed = new List<string>(3);
        if (!string.Equals(SelectedMonitorDeviceName, other.SelectedMonitorDeviceName, StringComparison.Ordinal))
            changed.Add(nameof(AmbientSettings.SelectedMonitorDeviceName));
        if (UseMultiLight != other.UseMultiLight)
            changed.Add(nameof(AmbientSettings.UseMultiLight));
        if (PerformanceMode != other.PerformanceMode)
            changed.Add(nameof(PerformanceMode));
        return string.Join(",", changed);
    }
}

namespace Deckle.Lighting.Ambient;

/// <summary>
/// The settings <see cref="AmbientEngine"/> reads only when it starts: the
/// capture monitor and the pipeline shape. The app keeps the values a running
/// engine started with and restarts it when one of them changes, so every
/// setting acts without a manual off and on. The Hue bridge fields and the
/// group are also read only at start, but they are not part of this set:
/// changing them does not restart a running engine.
/// </summary>
public readonly record struct AmbientStartSettings(
    string? SelectedMonitorDeviceName,
    bool UseMultiLight)
{
    /// <summary>Reads the start-time values.</summary>
    public static AmbientStartSettings From(AmbientSettings settings)
        => new(
            settings.SelectedMonitorDeviceName,
            settings.UseMultiLight);

    /// <summary>Names of the start-time values that differ from
    /// <paramref name="other"/>, comma-separated, as
    /// <see cref="AmbientSettings"/> property names; empty when none
    /// differs.</summary>
    public string ChangedSettings(AmbientStartSettings other)
    {
        var changed = new List<string>(2);
        if (!string.Equals(SelectedMonitorDeviceName, other.SelectedMonitorDeviceName, StringComparison.Ordinal))
            changed.Add(nameof(AmbientSettings.SelectedMonitorDeviceName));
        if (UseMultiLight != other.UseMultiLight)
            changed.Add(nameof(AmbientSettings.UseMultiLight));
        return string.Join(",", changed);
    }
}

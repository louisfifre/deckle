namespace Deckle.Lighting.Ambient;

/// <summary>
/// The settings <see cref="AmbientEngine"/> reads only when it starts: the
/// capture monitor. The app keeps the value a running engine started with and
/// restarts it when it changes, so the setting acts without a manual off and
/// on. The pipeline shape used to be here too; it left with the single-colour
/// mode (2026-11-25). The Hue bridge fields and the
/// group are also read only at start, but they are not part of this set:
/// changing them does not restart a running engine.
/// </summary>
public readonly record struct AmbientStartSettings(
    string? SelectedMonitorDeviceName)
{
    /// <summary>Reads the start-time values.</summary>
    public static AmbientStartSettings From(AmbientSettings settings)
        => new(settings.SelectedMonitorDeviceName);

    /// <summary>Names of the start-time values that differ from
    /// <paramref name="other"/>, comma-separated, as
    /// <see cref="AmbientSettings"/> property names; empty when none
    /// differs.</summary>
    public string ChangedSettings(AmbientStartSettings other)
    {
        var changed = new List<string>(1);
        if (!string.Equals(SelectedMonitorDeviceName, other.SelectedMonitorDeviceName, StringComparison.Ordinal))
            changed.Add(nameof(AmbientSettings.SelectedMonitorDeviceName));
        return string.Join(",", changed);
    }
}

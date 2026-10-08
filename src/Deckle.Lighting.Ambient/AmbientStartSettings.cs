namespace Deckle.Lighting.Ambient;

/// <summary>
/// The settings <see cref="AmbientEngine"/> reads only when it starts: the
/// capture monitor, the pipeline shape and the capture rate. The app keeps
/// the values a running engine started with and restarts it when one of
/// them changes, so every setting acts without a manual off and on.
/// </summary>
public readonly record struct AmbientStartSettings(
    string? SelectedMonitorDeviceName,
    bool UseMultiLight,
    int CaptureFrameRateHz)
{
    /// <summary>Reads the start-time values, with the capture rate snapped
    /// the way the engine snaps it, so an unsnapped stored value does not
    /// look like a change.</summary>
    public static AmbientStartSettings From(AmbientSettings settings)
        => new(
            settings.SelectedMonitorDeviceName,
            settings.UseMultiLight,
            AmbientCaptureRate.Snap(settings.CaptureFrameRateHz));

    /// <summary>Names of the <see cref="AmbientSettings"/> properties that
    /// differ from <paramref name="other"/>, comma-separated; empty when
    /// none differs.</summary>
    public string ChangedSettings(AmbientStartSettings other)
    {
        var changed = new List<string>(3);
        if (!string.Equals(SelectedMonitorDeviceName, other.SelectedMonitorDeviceName, StringComparison.Ordinal))
            changed.Add(nameof(AmbientSettings.SelectedMonitorDeviceName));
        if (UseMultiLight != other.UseMultiLight)
            changed.Add(nameof(AmbientSettings.UseMultiLight));
        if (CaptureFrameRateHz != other.CaptureFrameRateHz)
            changed.Add(nameof(AmbientSettings.CaptureFrameRateHz));
        return string.Join(",", changed);
    }
}

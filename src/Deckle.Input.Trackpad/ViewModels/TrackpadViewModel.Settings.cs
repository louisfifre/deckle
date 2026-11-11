using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Input.Trackpad;

// ── TrackpadViewModel — settings manifest ─────────────────────────────────────
//
// The declarative half of TrackpadPage's persisted settings, kept beside the
// ViewModel that owns the values; SettingsComposer turns each list into
// SettingsCards.
//
// Two flat manifests, one per page region, not a master-child group: each
// setting is an independent leaf. The master is the module's on/off switch,
// first on the page and outside any section, so it has its own list. The
// raw-frame recording lives in TrackpadTelemetryViewModel, composed on the
// Diagnostics page.
//
// Every descriptor's default reads the POCO initializer (new TrackpadSettings()
// .<Field>), the same literal TrackpadSettingsService persists, so each card's
// reset goes active exactly when the value leaves that default.
public partial class TrackpadViewModel
{
    public IReadOnlyList<SettingDescriptor> TrackpadEnabledSettingsManifest =>
    [
        Setting.Toggle("TrackpadPage_DragCard",
            () => Enabled,
            value => Enabled = value,
            defaultValue: () => new TrackpadSettings().Enabled),
    ];

    public IReadOnlyList<SettingDescriptor> TrackpadDragSettingsManifest =>
    [
        Setting.Magnitude("TrackpadPage_SpeedCard",
            () => DragSpeed,
            value => DragSpeed = value,
            new MagnitudeArgs(0.25, 3.0, Unit: "×"),
            defaultValue: () => new TrackpadSettings().DragSpeed),

        // Tuned by feel against stray clicks, so it keeps the slider; the
        // range gives a 5 ms grain.
        Setting.Magnitude("TrackpadPage_PressDelayCard",
            () => PressDelayMs,
            value => PressDelayMs = value,
            new MagnitudeArgs(TrackpadSettings.MinPressDelayMs, TrackpadSettings.MaxPressDelayMs,
                Unit: "ms", Integer: true),
            defaultValue: () => new TrackpadSettings().PressDelayMs),

        // The delay's partner: the press lands at whichever is met first.
        // Tuned by feel too, so it gets the same slider.
        Setting.Magnitude("TrackpadPage_PressDistanceCard",
            () => PressDistancePx,
            value => PressDistancePx = value,
            new MagnitudeArgs(TrackpadSettings.MinPressDistancePx, TrackpadSettings.MaxPressDistancePx,
                Unit: "px", Integer: true),
            defaultValue: () => new TrackpadSettings().PressDistancePx),
    ];
}

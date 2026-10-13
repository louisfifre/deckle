using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Input.Trackpad;

// ── TrackpadViewModel — settings manifest ─────────────────────────────────────
//
// The declarative half of TrackpadPage's persisted settings, kept beside the
// ViewModel that owns the values; SettingsComposer turns each list into
// SettingsCards.
//
// Three flat manifests, one per page region, not a master-child group: each
// setting is an independent leaf. The master is the module's on/off switch,
// first on the page and outside any section, so it has its own list. Raw-frame
// recording stays independent of the master: it captures with the recognizer
// off.
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
            glyph: Glyphs.Trackpad,
            defaultValue: () => new TrackpadSettings().Enabled),
    ];

    public IReadOnlyList<SettingDescriptor> TrackpadDragSettingsManifest =>
    [
        Setting.Magnitude("TrackpadPage_SpeedCard",
            () => DragSpeed,
            value => DragSpeed = value,
            new MagnitudeArgs(0.25, 3.0, Unit: "×"),
            glyph: Glyphs.Tuning,
            defaultValue: () => new TrackpadSettings().DragSpeed),
    ];

    public IReadOnlyList<SettingDescriptor> TrackpadDiagnosticsSettingsManifest =>
    [
        Setting.Toggle("TrackpadPage_RecordFramesCard",
            () => RecordFrames,
            value => RecordFrames = value,
            glyph: Glyphs.AudioRecording,
            defaultValue: () => new TrackpadSettings().RecordFrames),
    ];
}

using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Input.Trackpad;

// The declarative half of TrackpadPage's persisted settings. Each entry declares
// one setting — its kind, its localization key in this module's .resw, its glyph,
// and typed selectors onto this VM's own properties — and SettingsComposer turns
// the list into SettingsCards.
//
// Two flat manifests, one per page section — NOT a master-child group: each
// setting is an independent leaf, so the drag-speed card stays live while the
// master (Enabled) is off. "Diagnostics" holds the raw-frame recording, which
// captures with the recognizer off.
//
// Every descriptor's default reads the POCO initializer (new TrackpadSettings()
// .<Field>) — the same literal TrackpadSettingsService persists — so each card
// gets a per-card reset that goes active exactly when the value leaves that
// default.
public partial class TrackpadViewModel
{
    public IReadOnlyList<SettingDescriptor> TrackpadDragSettingsManifest =>
    [
        Setting.Toggle("TrackpadPage_DragCard",
            () => Enabled,
            value => Enabled = value,
            glyph: Glyphs.Trackpad,
            defaultValue: () => new TrackpadSettings().Enabled),

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

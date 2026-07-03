using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Input.Trackpad;

// ── TrackpadViewModel — settings manifest ─────────────────────────────────────
//
// The declarative half of TrackpadPage's persisted settings, kept beside the
// ViewModel that owns the values rather than in the page code-behind. Each entry
// declares one setting — its kind, its localization key (the SAME x:Uid the
// hand-authored card carried, so the composer resolves the identical Header and
// Description from this module's .resw), its glyph, and typed selectors onto this
// VM's own properties — and SettingsComposer turns the list into SettingsCards.
//
// Two flat manifests, one per page section — NOT a master-child group. The
// hand-authored page greyed the drag-speed card while the master (Enabled) was
// off; the composer drops that coupling by decision, composing each setting as an
// independent leaf. Splitting into two lists mirrors the page's two section
// headers: "Three-finger drag" holds the master and its speed, "Diagnostics"
// holds the raw-frame recording — always independent of the master (it captures
// with the recognizer off). Each section is hosted by its own composer so the
// on-screen section order is preserved.
//
// Only the persisted settings live here. The Windows-integration acts (neutralize
// / repair / start elevated) stay hand-authored on the page — they are imperative
// commands with their own success reporting, not settable values.
//
// Every descriptor's default reads the POCO initializer (new TrackpadSettings()
// .<Field>) — the same literal TrackpadSettingsService persists — so each card
// gets a per-card reset that goes active exactly when the value leaves that
// default.
public partial class TrackpadViewModel
{
    // "Three-finger drag" section — the master switch and its drag-speed slider.
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

    // "Diagnostics" section — raw-frame recording, independent of the master.
    public IReadOnlyList<SettingDescriptor> TrackpadDiagnosticsSettingsManifest =>
    [
        Setting.Toggle("TrackpadPage_RecordFramesCard",
            () => RecordFrames,
            value => RecordFrames = value,
            glyph: Glyphs.AudioRecording,
            defaultValue: () => new TrackpadSettings().RecordFrames),
    ];
}

using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── DiagnosticsViewModel — settings manifest ──────────────────────────────────
//
// The declarative half of the page, kept beside the ViewModel that owns the
// values rather than in the page code-behind. Each entry declares one setting —
// its kind, its localization key, its glyph, and typed selectors onto this VM's
// own properties — and SettingsComposer turns the list into SettingsCards.
//
// This is the "declare near the code, the surface composes itself" shape: the
// declaration lives with the values (same class, separate file — the per-module
// manifest), and the page only hosts. Adding or moving a setting is editing this
// list, never hand-authoring a card in XAML.
public partial class DiagnosticsViewModel
{
    // Runtime emission filters (the "Logging" section). Plain toggles today; the
    // manifest grows with the section. Selectors point straight at this VM's
    // properties — the change handlers (OnXChanged → PushLoggingToSettings) and
    // persistence are unchanged, the composer only drives the UI.
    public IReadOnlyList<SettingDescriptor> LoggingSettings =>
    [
        Setting.Toggle("LoggingAmbientCard",
            () => LogAmbientCaptureActivity,
            value => LogAmbientCaptureActivity = value,
            glyph: Glyphs.Lightbulb),
        Setting.Toggle("LoggingStreamingCard",
            () => LogStreamingTranscriptionActivity,
            value => LogStreamingTranscriptionActivity = value,
            glyph: Glyphs.Speech),
        Setting.Toggle("LoggingAutocorrectCard",
            () => LogAutocorrectActivity,
            value => LogAutocorrectActivity = value,
            glyph: Glyphs.Language),
        Setting.Toggle("LoggingWindowingCard",
            () => LogWindowingActivity,
            value => LogWindowingActivity = value,
            glyph: Glyphs.Window),
    ];

    // Telemetry opt-ins (the "Telemetry" section). Only the Latency toggle is
    // composable: it is a plain TwoWay switch with no side effect beyond the VM
    // setter. The other telemetry rows stay hand-authored in the page — each
    // carries an off→on consent dialog (Application log, Microphone, Corpus,
    // Audio corpus), a nested expander layout, a RadioButtons choice, or a folder
    // path — none expressible by a plain Toggle descriptor. So this single
    // contiguous run is hosted on its own between the bespoke cards.
    public IReadOnlyList<SettingDescriptor> TelemetrySettings =>
    [
        Setting.Toggle("GeneralLatencyCard",
            () => TelemetryLatencyEnabled,
            value => TelemetryLatencyEnabled = value,
            glyph: Glyphs.Latency),
    ];
}

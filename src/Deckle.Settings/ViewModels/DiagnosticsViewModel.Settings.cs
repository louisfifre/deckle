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

    // Telemetry opt-ins (the "Telemetry" section). Three composable toggles now:
    // the two consent opt-ins (Application log, Microphone) carry a confirmOnEnable
    // gate so the composer holds their OFF→ON write behind the consent dialog —
    // exactly the off→on-shows-a-dialog flow the hand-authored cards ran, now
    // declared rather than wired in the page. Latency is a plain TwoWay switch.
    // Declared in on-screen order (Application log first by user request, then
    // Microphone, then Latency), so the composed host reproduces the former card
    // order. The remaining telemetry rows stay hand-authored in the page — the
    // Corpus expander, the Audio-corpus RadioButtons choice, the storage folder
    // path — none expressible by a plain Toggle descriptor.
    //
    // No defaultValue on the consent toggles: a privacy opt-in has no "resettable
    // default" affordance per row (the section "Reset" clears them), so the composer
    // renders no per-card reset wheel for them — which is correct.
    public IReadOnlyList<SettingDescriptor> TelemetrySettings =>
    [
        Setting.Toggle("GeneralAppLogCard",
            () => ApplicationLogToDisk,
            value => ApplicationLogToDisk = value,
            glyph: Glyphs.AppLog,
            confirmOnEnable: root => ApplicationLogConsentDialog.ShowAsync(root)),
        Setting.Toggle("GeneralLogMicrophoneCard",
            () => MicrophoneTelemetry,
            value => MicrophoneTelemetry = value,
            glyph: Glyphs.Microphone,
            confirmOnEnable: root => MicrophoneTelemetryConsentDialog.ShowAsync(root)),
        Setting.Toggle("GeneralLatencyCard",
            () => TelemetryLatencyEnabled,
            value => TelemetryLatencyEnabled = value,
            glyph: Glyphs.Latency),
    ];
}

using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Audio;

// ── RecordingViewModel — settings manifest ────────────────────────────────────
//
// The declarative half of RecordingPage, kept beside the ViewModel that owns the
// values rather than in the page code-behind. Each entry declares one setting —
// its kind, its localization key, its glyph, and typed selectors onto this VM's
// own properties — and SettingsComposer turns the list into SettingsCards.
//
// The microphone ComboBox (runtime waveIn enumeration, the page's untitled top
// card) and the mic-check and listening-test commands + InfoBars (diagnostic
// readouts in the Checks section, not values) stay hand-authored in the page —
// neither fits the flat get/set descriptor model.
public partial class RecordingViewModel
{
    // Voice level — a number field in dBFS, no slider: the value is read
    // against a microphone, not dragged by feel. The default reads the POCO
    // initializer, like every other default here.
    public IReadOnlyList<SettingDescriptor> VoiceLevelSettings =>
    [
        Setting.Magnitude("RecordingVoiceLevelCard",
            () => VoiceLevelDbfs,
            value => VoiceLevelDbfs = value,
            new MagnitudeArgs(-90, 0, Unit: "dBFS", Slider: false),
            defaultValue: () => new CaptureSettings().VoiceLevelDbfs),
    ];

    // Maximum recording length — a ComboBox of fixed durations, the Windows 11
    // Settings idiom for a timeout with a "never" state (Screen and sleep). Values
    // are seconds, the POCO's unit, so the VM needs no projection. 0 is the explicit
    // "No limit" option and the default, read from the POCO initializer like every
    // other default here. A stored value outside the list (hand-edited file) shows
    // as no selection, as Windows does.
    public IReadOnlyList<SettingDescriptor> RecordingLimitSettings =>
    [
        Setting.Choice<int>("RecordingLimitCard",
            () => MaxRecordingDurationSeconds,
            value => MaxRecordingDurationSeconds = value,
            [
                (0, "RecordingLimitNone"),
                (600, "RecordingLimit10Min"),
                (1200, "RecordingLimit20Min"),
                (1800, "RecordingLimit30Min"),
                (3600, "RecordingLimit1Hour"),
                (7200, "RecordingLimit2Hours"),
            ],
            defaultValue: () => new CaptureSettings().MaxRecordingDurationSeconds),
    ];

    // Transcription pre-processing (the DSP black box). A single opt-in toggle.
    // The default selector reads the POCO initializer (new PreprocessingSettings().Enabled),
    // so the resettable default and the persisted default are the one same literal —
    // the per-card reset goes active exactly when the toggle leaves that value.
    public IReadOnlyList<SettingDescriptor> PreprocessingSettings =>
    [
        Setting.Toggle("RecordingPagePreprocessingCard",
            () => PreprocessingEnabled,
            value => PreprocessingEnabled = value,
            defaultValue: () => new PreprocessingSettings().Enabled),
    ];

    // The mic level check above measures the RMS of a 5 s sample and compares
    // it to the preprocessing target, to advise whether turning the DSP on is
    // worth it, and says how far it sits above or below the voice level. It
    // sets nothing.
    //
    // The microphone-telemetry opt-in lives in RecordingTelemetryViewModel,
    // composed on the Diagnostics page.
}

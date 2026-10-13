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
// The microphone ComboBox (runtime waveIn enumeration) and the mic-check and
// listening-test commands + InfoBars (diagnostic readouts, not values) stay
// hand-authored in the page — neither fits the flat get/set descriptor model.
public partial class RecordingViewModel
{
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
            glyph: Glyphs.Latency,
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
            glyph: Glyphs.AudioRecording,
            defaultValue: () => new PreprocessingSettings().Enabled),
    ];

    // Voice level window — the calibration group, with INVERTED master semantics.
    // The fold's reveal model shows children when the master is ON, hides them when
    // OFF. Here the three sliders are the MANUAL window, and they matter precisely
    // when auto-calibration is OFF (auto overwrites them from measured percentiles).
    // So the master is "set the level window manually" — true when auto is off — and
    // the selectors project the inverse of LevelWindowAutoCalibration. Only this
    // projection flips the bool; the VM property keeps auto-calibration semantics.
    // The child setters carry the live AudioLevelMapper push.
    //
    // Defaults read the POCO initializer (new LevelWindowSettings().<Field>) — the
    // single source of truth. The master default mirrors the master's own inversion:
    // master = "set manually" = !AutoCalibrationEnabled, so its default is
    // !new LevelWindowSettings().AutoCalibrationEnabled. The group's reset thus
    // restores the shipping calibration window and the POCO's auto-calibration
    // default (off) in one gesture.
    public IReadOnlyList<SettingDescriptor> VoiceLevelSettings =>
    [
        Setting.Group("GeneralVoiceLevelExpander",
            () => !LevelWindowAutoCalibration,
            value => LevelWindowAutoCalibration = !value,
            [
                Setting.Magnitude("GeneralVoiceLevelFloorCard",
                    () => LevelWindowMinDbfs,
                    value => LevelWindowMinDbfs = value,
                    new MagnitudeArgs(-90, -10, Unit: "dBFS"),
                    defaultValue: () => new LevelWindowSettings().MinDbfs),
                Setting.Magnitude("GeneralVoiceLevelCeilingCard",
                    () => LevelWindowMaxDbfs,
                    value => LevelWindowMaxDbfs = value,
                    new MagnitudeArgs(-60, -10, Unit: "dBFS"),
                    defaultValue: () => new LevelWindowSettings().MaxDbfs),
                Setting.Magnitude("GeneralVoiceLevelCurveCard",
                    () => LevelWindowExponent,
                    value => LevelWindowExponent = value,
                    new MagnitudeArgs(0.3, 3.0),
                    defaultValue: () => new LevelWindowSettings().DbfsCurveExponent),
            ],
            glyph: Glyphs.VoiceLevel,
            defaultValue: () => !new LevelWindowSettings().AutoCalibrationEnabled),
    ];

    // Microphone telemetry — the per-recording RMS-distribution opt-in. One toggle,
    // gated by the shell's consent dialog through the Catalog registry
    // (TelemetryConsent.RequestMicrophone, a method group the App wires at boot) —
    // so the module gates its enable behind consent without referencing the shell.
    // No defaultValue: a privacy opt-in carries no per-card reset affordance, and
    // this section hosts no section-reset link either.
    public IReadOnlyList<SettingDescriptor> TelemetrySettings =>
    [
        Setting.Toggle("RecordingMicrophoneTelemetryCard",
            () => MicrophoneTelemetry,
            value => MicrophoneTelemetry = value,
            glyph: Glyphs.Microphone,
            confirmOnEnable: TelemetryConsent.RequestMicrophone),
    ];
}

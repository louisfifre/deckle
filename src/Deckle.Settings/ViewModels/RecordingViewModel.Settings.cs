using System.Collections.Generic;
using Deckle.Audio;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── RecordingViewModel — settings manifest ────────────────────────────────────
//
// The declarative half of RecordingPage, kept beside the ViewModel that owns the
// values rather than in the page code-behind. Each entry declares one setting —
// its kind, its localization key, its glyph, and typed selectors onto this VM's
// own properties — and SettingsComposer turns the list into SettingsCards.
//
// The microphone ComboBox (runtime waveIn enumeration) and the mic-check command
// + InfoBars (diagnostic readouts, not values) stay hand-authored in the page —
// neither fits the flat get/set descriptor model. The capture-pipeline toggle and
// the voice-level window migrate here.
public partial class RecordingViewModel
{
    // Transcription pre-processing (the DSP black box). A single opt-in toggle;
    // the change handler (OnPreprocessingEnabledChanged → PushToSettings) and
    // persistence are unchanged, the composer only drives the UI. Reuses the
    // existing x:Uid as the localization key.
    //
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
    // the selectors project the inverse of LevelWindowAutoCalibration. The VM
    // property and its OnLevelWindowAutoCalibrationChanged side effect are unchanged;
    // only this projection flips the bool. The child setters carry the live
    // AudioLevelMapper push (SettingsHost.ApplyLevelWindow) untouched. Bounds copied
    // verbatim from the former hand-authored SettingsExpander.
    //
    // Defaults read the POCO initializer (new LevelWindowSettings().<Field>) — the
    // single source of truth, the same literals SettingsService persists. The master
    // default mirrors the master's own inversion: master = "set manually" =
    // !AutoCalibrationEnabled, so its default is !new LevelWindowSettings().
    // AutoCalibrationEnabled. Each child slider takes its raw field as a double; the
    // float→double widening is implicit. The group's reset thus restores the shipping
    // calibration window and re-arms auto-calibration in one gesture.
    public IReadOnlyList<SettingDescriptor> VoiceLevelSettings =>
    [
        Setting.Group("GeneralVoiceLevelExpander",
            () => !LevelWindowAutoCalibration,
            value => LevelWindowAutoCalibration = !value,
            [
                Setting.Slider("GeneralVoiceLevelFloorCard",
                    () => LevelWindowMinDbfs,
                    value => LevelWindowMinDbfs = value,
                    new SliderArgs(-90, -10, 1, Unit: "dBFS"),
                    defaultValue: () => new LevelWindowSettings().MinDbfs),
                Setting.Slider("GeneralVoiceLevelCeilingCard",
                    () => LevelWindowMaxDbfs,
                    value => LevelWindowMaxDbfs = value,
                    new SliderArgs(-60, -10, 1, Unit: "dBFS"),
                    defaultValue: () => new LevelWindowSettings().MaxDbfs),
                Setting.Slider("GeneralVoiceLevelCurveCard",
                    () => LevelWindowExponent,
                    value => LevelWindowExponent = value,
                    new SliderArgs(0.3, 3.0, 0.05),
                    defaultValue: () => new LevelWindowSettings().DbfsCurveExponent),
            ],
            glyph: Glyphs.VoiceLevel,
            defaultValue: () => !new LevelWindowSettings().AutoCalibrationEnabled),
    ];
}

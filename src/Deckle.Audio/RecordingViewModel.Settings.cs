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
// The microphone ComboBox (runtime PortAudio enumeration, the page's untitled top
// card) and the mic-check and listening-test commands + InfoBars (diagnostic
// readouts inside the pre-processing fold, not values) stay hand-authored in
// the page — neither fits the flat get/set descriptor model.
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

    // Maximum recording length — a group: the master switch "Limit recording
    // duration", off by default (the POCO's 0 = no limit), reveals one length in
    // minutes, a field with a slider (a length is swept by feel, unlike the voice
    // level). The POCO and the capture loop keep seconds; RecordingLimit and the
    // two VM accessors do the conversion. The child's default is the length the
    // switch restores, so the section reset returns the group to off.
    public IReadOnlyList<SettingDescriptor> RecordingLimitSettings =>
    [
        Setting.Group("RecordingLimitCard",
            () => LimitRecordingDuration,
            value => LimitRecordingDuration = value,
            [
                Setting.Magnitude("RecordingLimitDurationCard",
                    () => MaxRecordingDurationMinutes,
                    value => MaxRecordingDurationMinutes = value,
                    new MagnitudeArgs(RecordingLimit.MinMinutes, RecordingLimit.MaxMinutes,
                        Unit: "min", Integer: true),
                    defaultValue: () => RecordingLimit.DefaultMinutes),
            ],
            defaultValue: () => RecordingLimit.IsLimited(new CaptureSettings().MaxRecordingDurationSeconds)),
    ];

    // Exclusive access to the microphone, off by default (it locks every other
    // application out of the microphone while a dictation runs). The default
    // reads the POCO initializer.
    public IReadOnlyList<SettingDescriptor> ExclusiveModeSettings =>
    [
        Setting.Toggle("RecordingExclusiveModeCard",
            () => ExclusiveMode,
            value => ExclusiveMode = value,
            defaultValue: () => new CaptureSettings().ExclusiveMode),
    ];

    // Transcription pre-processing (the DSP black box), a single opt-in toggle
    // composed as the master of a fold with no composed child. The fold holds
    // the listening test, which the page adds to its items by hand (a command,
    // not a value). The composer gates only its own children on the master, so
    // the test stays visible with the toggle off: it is what tells the person
    // whether to turn it on (2026-11-07 review, change 3).
    // The default selector reads the POCO initializer (new PreprocessingSettings().Enabled),
    // so the resettable default and the persisted default are the one same literal —
    // the fold's reset goes active exactly when the toggle leaves that value.
    public IReadOnlyList<SettingDescriptor> PreprocessingSettings =>
    [
        Setting.Group("RecordingPagePreprocessingCard",
            () => PreprocessingEnabled,
            value => PreprocessingEnabled = value,
            [],
            defaultValue: () => new PreprocessingSettings().Enabled),
    ];

    // The mic level check, in the Voice section, measures the RMS of a 5 s sample and compares
    // it to the preprocessing target, to advise whether turning the DSP on is
    // worth it, and says how far it sits above or below the voice level. It
    // sets nothing.
    //
    // The microphone-telemetry opt-in lives in RecordingTelemetryViewModel,
    // composed on the Diagnostics page.
}

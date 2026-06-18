using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── RecordingViewModel — settings manifest ────────────────────────────────────
//
// The declarative half of RecordingPage, kept beside the ViewModel that owns the
// values rather than in the page code-behind. Each entry declares one setting —
// its kind, its localization key, its glyph, and typed selectors onto this VM's
// own properties — and SettingsComposer turns the list into SettingsCards.
//
// Only the standalone capture-pipeline toggle migrates here. The microphone
// ComboBox (runtime waveIn enumeration), the mic-check command + InfoBars
// (diagnostic readouts, not values), and the voice-level expander (sliders with
// AudioLevelMapper side effects, nested under a header toggle) stay hand-authored
// in the page — none fits the flat get/set descriptor model today.
public partial class RecordingViewModel
{
    // Transcription pre-processing (the DSP black box). A single opt-in toggle;
    // the change handler (OnPreprocessingEnabledChanged → PushToSettings) and
    // persistence are unchanged, the composer only drives the UI. Reuses the
    // existing x:Uid as the localization key.
    public IReadOnlyList<SettingDescriptor> PreprocessingSettings =>
    [
        Setting.Toggle("RecordingPagePreprocessingCard",
            () => PreprocessingEnabled,
            value => PreprocessingEnabled = value,
            glyph: Glyphs.AudioRecording),
    ];
}

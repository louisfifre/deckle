using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Settings;

namespace Deckle.Transcription;

// ── WhisperViewModel — settings manifest ──────────────────────────────────────
//
// The declarative half of WhisperPage's two activatable folds, kept beside the
// ViewModel that owns the values rather than in the page code-behind. Each entry
// declares one setting — its kind, its localization key (the SAME x:Uid the
// hand-authored card carried, so the composer resolves the identical Header and
// Description from this module's .resw), its glyph, and typed selectors onto this
// VM's own properties — and SettingsComposer turns the list into the expander's
// master toggle plus its child cards.
//
// Only the VAD and Streaming folds migrate. The flat cards and the other groups
// (Language, Model, Decoding, Confidence, output filters, MaxTokens) stay
// hand-authored — their selectors don't fit the flat get/set descriptor model, or
// they carry bespoke chrome (the model AutoSuggestBox, the restart footer).
//
// Single-source defaults on EVERY descriptor (master and children) read the POCO
// initializer (new SpeechTrimSettings().<Field>, new EnergySegmenterSettings()
// .<Field>) — the same literal SettingsService persists — so each migrated card
// gets a per-card reset that goes active exactly when the value leaves that
// default. The Seg* fields are int in the POCO; the int→double widening into the
// Func<double> default is implicit, the same way RecordingViewModel feeds its
// float LevelWindow fields.
public partial class WhisperViewModel
{
    // Voice activity detection — the Silero pre-trim fold. The master is the
    // VadEnabled toggle; the four detection parameters are its children, hidden by
    // the composer while the master is off (it composes the master into each
    // child's VisibleWhen — native masking, no per-child gate here). The bounds and
    // steps are copied verbatim from the former hand-authored sliders.
    //
    // The whole fold gates on StreamingEnabled at the GROUP's visibleWhen: the
    // Silero trim runs only on the streaming path, so the entire expander collapses
    // when streaming is off — exactly what the hand-authored card did with its
    // VisibleWhenStreaming bind on the expander itself.
    public IReadOnlyList<SettingDescriptor> VadSettings =>
    [
        Setting.Group("WhisperVadEnabledCard",
            () => VadEnabled,
            value => VadEnabled = value,
            [
                Setting.Slider("WhisperVadThresholdCard",
                    () => VadThreshold,
                    value => VadThreshold = value,
                    new SliderArgs(0.1, 0.9, 0.05),
                    defaultValue: () => new SpeechTrimSettings().Threshold),
                Setting.Slider("WhisperVadMinSpeechCard",
                    () => VadMinSpeechDurationMs,
                    value => VadMinSpeechDurationMs = value,
                    new SliderArgs(0, 1000, 50),
                    defaultValue: () => new SpeechTrimSettings().MinSpeechDurationMs),
                Setting.Slider("WhisperVadMinSilenceCard",
                    () => VadMinSilenceDurationMs,
                    value => VadMinSilenceDurationMs = value,
                    new SliderArgs(0, 1000, 50),
                    defaultValue: () => new SpeechTrimSettings().MinSilenceDurationMs),
                Setting.Slider("WhisperVadSpeechPadCard",
                    () => VadSpeechPadMs,
                    value => VadSpeechPadMs = value,
                    new SliderArgs(0, 200, 10),
                    defaultValue: () => new SpeechTrimSettings().SpeechPadMs),
            ],
            glyph: Glyphs.Microphone,
            visibleWhen: () => StreamingEnabled,
            defaultValue: () => new SpeechTrimSettings().Enabled),
    ];

    // Streaming pipeline — the energy-segmenter fold. The master is StreamingEnabled
    // (the user-facing on/off projected onto PipelineStrategyKind); the seven
    // segmenter parameters are its children, hidden by the composer while streaming
    // is off. They are Number cards (exact figures typed, not swept) — the same
    // NumberBoxes the hand-authored cards used, with their Min/Max/Small/Large
    // copied verbatim.
    //
    // The master's default mirrors the VM's own projection of the segmenter Strategy
    // onto the bool (Load/Push: StreamingEnabled = Strategy == Streaming), read off a
    // fresh StreamingSettings so the shipped default Strategy (Monolithic → false)
    // is the single source — no hand-copied bool.
    public IReadOnlyList<SettingDescriptor> StreamingSettings =>
    [
        Setting.Group("WhisperStreamingEnabledCard",
            () => StreamingEnabled,
            value => StreamingEnabled = value,
            [
                Setting.Number("WhisperSegThresholdCard",
                    () => SegThresholdDbfs,
                    value => SegThresholdDbfs = value,
                    new NumberArgs(-90, 0, 1, 5),
                    defaultValue: () => new EnergySegmenterSettings().ThresholdDbfs),
                Setting.Number("WhisperSegHangoverMaxCard",
                    () => SegHangoverMaxMs,
                    value => SegHangoverMaxMs = value,
                    new NumberArgs(500, 15000, 100, 500),
                    defaultValue: () => new EnergySegmenterSettings().HangoverMaxMs),
                Setting.Number("WhisperSegHangoverMinCard",
                    () => SegHangoverMinMs,
                    value => SegHangoverMinMs = value,
                    new NumberArgs(100, 2000, 50, 100),
                    defaultValue: () => new EnergySegmenterSettings().HangoverMinMs),
                Setting.Number("WhisperSegHangoverRampStartCard",
                    () => SegHangoverRampStartMs,
                    value => SegHangoverRampStartMs = value,
                    new NumberArgs(0, 600000, 5000, 30000),
                    defaultValue: () => new EnergySegmenterSettings().HangoverRampStartMs),
                Setting.Number("WhisperSegHangoverRampEndCard",
                    () => SegHangoverRampEndMs,
                    value => SegHangoverRampEndMs = value,
                    new NumberArgs(30000, 900000, 5000, 30000),
                    defaultValue: () => new EnergySegmenterSettings().HangoverRampEndMs),
                Setting.Number("WhisperSegMarginCard",
                    () => SegMarginMs,
                    value => SegMarginMs = value,
                    new NumberArgs(0, 1000, 50, 100),
                    defaultValue: () => new EnergySegmenterSettings().MarginMs),
                Setting.Number("WhisperSegMinUtteranceCard",
                    () => SegMinUtteranceMs,
                    value => SegMinUtteranceMs = value,
                    new NumberArgs(0, 2000, 50, 100),
                    defaultValue: () => new EnergySegmenterSettings().MinUtteranceMs),
            ],
            glyph: Glyphs.Tuning,
            defaultValue: () => new StreamingSettings().Strategy == PipelineStrategyKind.Streaming),
    ];
}

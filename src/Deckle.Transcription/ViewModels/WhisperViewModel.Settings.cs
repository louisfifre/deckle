using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Presets;
using Deckle.Settings;

namespace Deckle.Transcription;

// The settings manifests WhisperPage composes, kept beside the view model that
// owns the values. Each key is the card's x:Uid in this module's .resw.
// Language, the initial prompt and Model stay hand-authored on the page, where
// they carry bespoke chrome (the model AutoSuggestBox, the restart footer).
//
// Defaults read the POCO initializers (new SpeechTrimSettings().<Field>, ...),
// the same literals the settings services persist, so each per-card reset goes
// active exactly when the value leaves that default.
public partial class WhisperViewModel
{
    // The transcript panel side sits in the overlay group because the panel is
    // part of the HUD and follows its master toggle.
    public IReadOnlyList<SettingDescriptor> BehaviourSettings =>
    [
        Setting.Group("GeneralOverlayExpander",
            () => OverlayEnabled,
            value => OverlayEnabled = value,
            [
                Setting.Toggle("GeneralOverlayFadeCard",
                    () => OverlayFadeOnProximity,
                    value => OverlayFadeOnProximity = value,
                    defaultValue: () => new OverlaySettings().FadeOnProximity),
                Setting.Toggle("GeneralOverlayAnimationsCard",
                    () => OverlayAnimations,
                    value => OverlayAnimations = value,
                    defaultValue: () => new OverlaySettings().Animations),
                Setting.Choice<string>("GeneralOverlayPositionCard",
                    () => OverlayPosition,
                    value => OverlayPosition = value,
                    [
                        ("TopCenter", "GeneralOverlayPositionTop"),
                        ("BottomCenter", "GeneralOverlayPositionBottom"),
                    ],
                    // Normalized by the same rule Load() applies, so the reset
                    // targets a real picker option.
                    defaultValue: () =>
                        (new OverlaySettings().Position ?? "").StartsWith("Top")
                            ? "TopCenter"
                            : "BottomCenter"),
                Setting.Choice<string>("GeneralTranscriptPanelSideCard",
                    () => TranscriptPanelSide,
                    value => TranscriptPanelSide = value,
                    [
                        ("Left", "GeneralTranscriptPanelSideLeft"),
                        ("Right", "GeneralTranscriptPanelSideRight"),
                    ],
                    defaultValue: () => new OverlaySettings().TranscriptPanelSide),
            ],
            glyph: Glyphs.Overlay,
            defaultValue: () => new OverlaySettings().Enabled),

        Setting.Toggle("GeneralAutoPasteCard",
            () => AutoPasteEnabled,
            value => AutoPasteEnabled = value,
            glyph: Glyphs.Paste,
            defaultValue: () => new PasteSettings().AutoPasteEnabled),
    ];

    // UseGpu needs a restart. The page's restart footer watches UseGpu on this
    // view model, which the composed toggle writes, so the toggle still trips it.
    public IReadOnlyList<SettingDescriptor> UseGpuSettingsManifest =>
    [
        Setting.Toggle("WhisperUseGpuCard",
            () => UseGpu,
            value => UseGpu = value,
            glyph: Glyphs.Gpu,
            defaultValue: () => new EngineSettings().UseGpu),
    ];

    // The POCO default "" means AppPaths.ModelsDirectory, which DefaultPath shows,
    // so the reset goes active only once the user has repointed the folder.
    public IReadOnlyList<SettingDescriptor> ModelsDirectorySettingsManifest =>
    [
        Setting.Path("WhisperModelsDirCard",
            () => ModelsDirectory,
            value => ModelsDirectory = value,
            new PathArgs(FolderPickerMode.Editable, DefaultPath: () => AppPaths.ModelsDirectory),
            glyph: Glyphs.Folder,
            defaultValue: () => new TranscriptionSettings().ModelsDirectory),
    ];

    // The composer hides a group's children while its master is off, so the
    // children carry no gate of their own. The whole fold hides with streaming
    // off because the Silero trim runs only on the streaming path.
    public IReadOnlyList<SettingDescriptor> VadSettings =>
    [
        Setting.Group("WhisperVadEnabledCard",
            () => VadEnabled,
            value => VadEnabled = value,
            [
                Setting.Magnitude("WhisperVadThresholdCard",
                    () => VadThreshold,
                    value => VadThreshold = value,
                    new MagnitudeArgs(0.1, 0.9),
                    defaultValue: () => new SpeechTrimSettings().Threshold),
                Setting.Magnitude("WhisperVadMinSpeechCard",
                    () => VadMinSpeechDurationMs,
                    value => VadMinSpeechDurationMs = value,
                    new MagnitudeArgs(0, 1000, Integer: true),
                    defaultValue: () => new SpeechTrimSettings().MinSpeechDurationMs),
                Setting.Magnitude("WhisperVadMinSilenceCard",
                    () => VadMinSilenceDurationMs,
                    value => VadMinSilenceDurationMs = value,
                    new MagnitudeArgs(0, 1000, Integer: true),
                    defaultValue: () => new SpeechTrimSettings().MinSilenceDurationMs),
                Setting.Magnitude("WhisperVadSpeechPadCard",
                    () => VadSpeechPadMs,
                    value => VadSpeechPadMs = value,
                    new MagnitudeArgs(0, 200, Integer: true),
                    defaultValue: () => new SpeechTrimSettings().SpeechPadMs),
            ],
            glyph: Glyphs.Microphone,
            visibleWhen: () => StreamingEnabled,
            defaultValue: () => new SpeechTrimSettings().Enabled),
    ];

    // The switch is a plain card, first and alone; everything under it hides
    // while streaming is off. The numeric cards are field-only (Slider: false):
    // exact figures are typed, not swept.
    //
    // A cut preset carries the eight hangover values: the four anchors and the
    // four curve control points. It does not carry the threshold (mic-dependent)
    // nor the margin and minimum utterance (blip rules). Any hand edit of the
    // eight values switches the selector to Custom, and choosing Custom again
    // writes the kept CutCustomValues back. The fold's reset returns the selector
    // to CutPresets.Default, which equals the shipped EnergySegmenterSettings
    // defaults.
    //
    // The switch's default reads the VM's projection of the segmenter Strategy off
    // a fresh StreamingSettings, so the shipped Strategy stays the single source.
    public IReadOnlyList<SettingDescriptor> StreamingSettings =>
    [
        Setting.Toggle("WhisperStreamingEnabledCard",
            () => StreamingEnabled,
            value => StreamingEnabled = value,
            glyph: Glyphs.Tuning,
            defaultValue: () => new StreamingSettings().Strategy == PipelineStrategyKind.Streaming),
        Setting.Magnitude("WhisperSegThresholdCard",
            () => SegThresholdDbfs,
            value => SegThresholdDbfs = value,
            new MagnitudeArgs(-90, 0, Slider: false),
            visibleWhen: () => StreamingEnabled,
            defaultValue: () => new EnergySegmenterSettings().ThresholdDbfs),
        Setting.Mode<string>("WhisperCutPresetCard",
            () => CutPreset,
            value => CutPreset = value,
            _cutPresets.SelectorOptions("WhisperCutPresetCustom"),
            [
                Setting.Magnitude("WhisperSegHangoverMaxCard",
                    () => SegHangoverMaxMs,
                    value => SegHangoverMaxMs = value,
                    new MagnitudeArgs(500, 15000, Slider: false, Integer: true),
                    visibleWhen: () => CutPreset == PresetFamily.Custom,
                    defaultValue: () => new EnergySegmenterSettings().HangoverMaxMs),
                Setting.Magnitude("WhisperSegHangoverMinCard",
                    () => SegHangoverMinMs,
                    value => SegHangoverMinMs = value,
                    new MagnitudeArgs(100, 2000, Slider: false, Integer: true),
                    visibleWhen: () => CutPreset == PresetFamily.Custom,
                    defaultValue: () => new EnergySegmenterSettings().HangoverMinMs),
                Setting.Magnitude("WhisperSegHangoverRampStartCard",
                    () => SegHangoverRampStartMs,
                    value => SegHangoverRampStartMs = value,
                    new MagnitudeArgs(0, 600000, Slider: false, Integer: true),
                    visibleWhen: () => CutPreset == PresetFamily.Custom,
                    defaultValue: () => new EnergySegmenterSettings().HangoverRampStartMs),
                Setting.Magnitude("WhisperSegHangoverRampEndCard",
                    () => SegHangoverRampEndMs,
                    value => SegHangoverRampEndMs = value,
                    new MagnitudeArgs(30000, 900000, Slider: false, Integer: true),
                    visibleWhen: () => CutPreset == PresetFamily.Custom,
                    defaultValue: () => new EnergySegmenterSettings().HangoverRampEndMs),
                // The instrument last and full width.
                Setting.Editor<CutCurveValues>("WhisperCutCurveCard",
                    ReadCutValues,
                    WriteCutValues,
                    new EditorArgs(() => new CutCurveEditor(this)),
                    visibleWhen: () => CutPreset == PresetFamily.Custom,
                    defaultValue: () => CutCurveValues.From(new EnergySegmenterSettings())),
            ],
            glyph: Glyphs.Tuning,
            visibleWhen: () => StreamingEnabled,
            defaultValue: () => CutPresets.Default),
        Setting.Magnitude("WhisperSegMarginCard",
            () => SegMarginMs,
            value => SegMarginMs = value,
            new MagnitudeArgs(0, 1000, Slider: false, Integer: true),
            visibleWhen: () => StreamingEnabled,
            defaultValue: () => new EnergySegmenterSettings().MarginMs),
        Setting.Magnitude("WhisperSegMinUtteranceCard",
            () => SegMinUtteranceMs,
            value => SegMinUtteranceMs = value,
            new MagnitudeArgs(0, 2000, Slider: false, Integer: true),
            visibleWhen: () => StreamingEnabled,
            defaultValue: () => new EnergySegmenterSettings().MinUtteranceMs),
    ];

    // The composed TextBox has no x:Uid of its own, so the SuppressRegex
    // placeholder is resolved here through Loc.GetFromOptional.
    public IReadOnlyList<SettingDescriptor> OutputFilterSettingsManifest =>
    [
        Setting.Toggle("WhisperSuppressNstCard",
            () => SuppressNonSpeechTokens,
            value => SuppressNonSpeechTokens = value,
            glyph: Glyphs.Filter,
            defaultValue: () => new OutputFilterSettings().SuppressNonSpeechTokens),
        Setting.Toggle("WhisperSuppressBlankCard",
            () => SuppressBlank,
            value => SuppressBlank = value,
            glyph: Glyphs.Filter,
            defaultValue: () => new OutputFilterSettings().SuppressBlank),
        Setting.Text("WhisperSuppressRegexCard",
            () => SuppressRegex,
            value => SuppressRegex = value,
            new TextArgs(Placeholder: Loc.GetFromOptional("Deckle.Transcription", "WhisperSuppressRegexBox/PlaceholderText")),
            glyph: Glyphs.Pattern,
            defaultValue: () => new OutputFilterSettings().SuppressRegex),
    ];

    public IReadOnlyList<SettingDescriptor> ContextSettingsManifest =>
    [
        Setting.Toggle("WhisperUseContextCard",
            () => UseContext,
            value => UseContext = value,
            glyph: Glyphs.Context,
            defaultValue: () => new ContextSettings().UseContext),
        Setting.Magnitude("WhisperMaxTokensCard",
            () => MaxTokens,
            value => MaxTokens = value,
            new MagnitudeArgs(-1, 448, Slider: false, Integer: true),
            glyph: Glyphs.Tokens,
            defaultValue: () => new ContextSettings().MaxTokens),
    ];

    public IReadOnlyList<SettingDescriptor> DecodingSettingsManifest =>
    [
        Setting.Section("WhisperDecodingExpander",
            [
                Setting.Toggle("WhisperUseBeamSearchCard",
                    () => UseBeamSearch,
                    value => UseBeamSearch = value,
                    defaultValue: () => new DecodingSettings().UseBeamSearch),
                Setting.Magnitude("WhisperBeamSizeCard",
                    () => BeamSize,
                    value => BeamSize = value,
                    new MagnitudeArgs(1, 10, Slider: false, Integer: true),
                    visibleWhen: () => UseBeamSearch,
                    defaultValue: () => new DecodingSettings().BeamSize),
                Setting.Magnitude("WhisperTemperatureCard",
                    () => Temperature,
                    value => Temperature = value,
                    new MagnitudeArgs(0, 1),
                    defaultValue: () => new DecodingSettings().Temperature),
                Setting.Magnitude("WhisperTemperatureIncrementCard",
                    () => TemperatureIncrement,
                    value => TemperatureIncrement = value,
                    new MagnitudeArgs(0, 1),
                    defaultValue: () => new DecodingSettings().TemperatureIncrement,
                    advisory: () => TemperatureIncrement == 0
                        ? Loc.GetFromOptional("Deckle.Transcription", "WhisperTemperatureIncrementWarning/Message")
                        : null),
            ],
            glyph: Glyphs.Tuning),
    ];

    public IReadOnlyList<SettingDescriptor> ConfidenceSettingsManifest =>
    [
        Setting.Section("WhisperConfidenceExpander",
            [
                Setting.Magnitude("WhisperEntropyCard",
                    () => EntropyThreshold,
                    value => EntropyThreshold = value,
                    new MagnitudeArgs(1.5, 3.5),
                    defaultValue: () => new ConfidenceSettings().EntropyThreshold),
                Setting.Magnitude("WhisperLogprobCard",
                    () => LogprobThreshold,
                    value => LogprobThreshold = value,
                    new MagnitudeArgs(-1.5, -0.4),
                    defaultValue: () => new ConfidenceSettings().LogprobThreshold),
                Setting.Magnitude("WhisperNoSpeechCard",
                    () => NoSpeechThreshold,
                    value => NoSpeechThreshold = value,
                    new MagnitudeArgs(0.05, 0.80),
                    defaultValue: () => new ConfidenceSettings().NoSpeechThreshold),
            ],
            glyph: Glyphs.Diagnostics),
    ];

    // No defaultValue anywhere: a privacy or observability opt-in has no
    // resettable default, so the composer renders no per-card reset. The consent
    // dialogs go through the Catalog registry (TelemetryConsent), so this module
    // gates its enables behind consent without referencing the shell.
    public IReadOnlyList<SettingDescriptor> DiagnosticsSettings =>
    [
        Setting.Toggle("GeneralLatencyCard",
            () => TelemetryLatencyEnabled,
            value => TelemetryLatencyEnabled = value,
            glyph: Glyphs.Latency),
        Setting.Group("GeneralCorpusExpander",
            () => TelemetryCorpusEnabled,
            value => TelemetryCorpusEnabled = value,
            glyph: Glyphs.AudioRecording,
            confirmOnEnable: TelemetryConsent.RequestCorpus,
            children:
            [
                Setting.Toggle("GeneralAudioCorpusCard",
                    () => RecordAudioCorpus,
                    value => RecordAudioCorpus = value,
                    confirmOnEnable: TelemetryConsent.RequestAudioCorpus),
                Setting.Radio("GeneralAudioCorpusContentCard",
                    () => AudioCorpusContentIndex,
                    value => AudioCorpusContentIndex = value,
                    options:
                    [
                        (0, "GeneralAudioCorpusContentMatch"),
                        (1, "GeneralAudioCorpusContentRaw"),
                    ],
                    visibleWhen: () => RecordAudioCorpus),
            ]),
    ];
}

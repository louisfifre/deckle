using System;
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
<<<<<<< HEAD
    // The switch's default reads the VM's projection of the segmenter Strategy off
    // a fresh StreamingSettings, so the shipped Strategy stays the single source.
    public IReadOnlyList<SettingDescriptor> StreamingSettings =>
=======
    // The switch's default mirrors the VM's own projection of the segmenter Strategy
    // onto the bool (Load/Push: StreamingEnabled = Strategy == Streaming), read off a
    // fresh StreamingSettings so the shipped default Strategy (Monolithic → false)
    // is the single source — no hand-copied bool.
    public IReadOnlyList<SettingDescriptor> StreamingSwitch =>
>>>>>>> main
    [
        Setting.Toggle("WhisperStreamingEnabledCard",
            () => StreamingEnabled,
            value => StreamingEnabled = value,
            glyph: Glyphs.Tuning,
            defaultValue: () => new StreamingSettings().Strategy == PipelineStrategyKind.Streaming),
    ];

    // The segmentation cards of the Streaming page, under the switch and the
    // speech-filtering fold (decision 16): the threshold, the cut mode fold, the
    // margin and the minimum utterance.
    public IReadOnlyList<SettingDescriptor> SegmentationSettings =>
    [
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
<<<<<<< HEAD
                // The instrument last and full width.
                Setting.Editor<CutCurveValues>("WhisperCutCurveCard",
                    ReadCutValues,
                    WriteCutValues,
                    new EditorArgs(() => new CutCurveEditor(this)),
=======
                // The instrument last and full width (decision 12): the shared
                // curve block (decision 21) over the four control points, drawn in
                // the frame the four anchor cards above set, with the sentence that
                // says what the curve does as its description.
                Setting.Curve("WhisperCutCurveCard",
                    () => new CurvePoints(SegCurveX1, SegCurveY1, SegCurveX2, SegCurveY2),
                    p => { SegCurveX1 = p.X1; SegCurveY1 = p.Y1; SegCurveX2 = p.X2; SegCurveY2 = p.Y2; },
                    new CurveArgs(Frame: CutCurveFrame, Describe: _ => CutCurveSentence()),
>>>>>>> main
                    visibleWhen: () => CutPreset == PresetFamily.Custom,
                    defaultValue: () =>
                    {
                        var d = new EnergySegmenterSettings();
                        return new CurvePoints(d.HangoverCurveX1, d.HangoverCurveY1, d.HangoverCurveX2, d.HangoverCurveY2);
                    }),
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

<<<<<<< HEAD
    // The composed TextBox has no x:Uid of its own, so the SuppressRegex
    // placeholder is resolved here through Loc.GetFromOptional.
=======
    // The frame of the cut curve: X is the open-utterance length in seconds, from
    // 0 to a little past the ramp end; Y is the decision delay in seconds, from 0
    // to the maximum hangover. The unit square runs from (ramp start, max) down
    // to (ramp end, min), the decay the segmenter eases between. The view model
    // keeps the anchors in milliseconds, the plot reads seconds.
    private CurveFrame CutCurveFrame()
    {
        double max = Math.Max(SegHangoverMaxMs / 1000.0, 1e-3);
        double min = Math.Min(SegHangoverMinMs / 1000.0, max);
        double start = Math.Max(0, SegHangoverRampStartMs / 1000.0);
        double end = Math.Max(start, SegHangoverRampEndMs / 1000.0);
        double xMax = end + Math.Max((end - start) * 0.15, 5.0);
        return new CurveFrame(start, max, end, min, xMax, max, Axes: true, XUnit: "s", YUnit: "s");
    }

    // The plain-language reading of the envelope the curve eases, the Playground
    // Segmentation page's sentence.
    private string CutCurveSentence()
    {
        double start = SegHangoverRampStartMs / 1000.0;
        double end = Math.Max(start, SegHangoverRampEndMs / 1000.0);
        return $"Waits {SegHangoverMaxMs / 1000.0:F1} s of silence for utterances up to {start:F0} s, " +
               $"then tightens to {SegHangoverMinMs / 1000.0:F1} s by {end:F0} s.";
    }

    // Output filters — the three flat cards under the "Output filters" section
    // header. No master toggle and no group: this section was hand-authored as a
    // run of independent SettingsCards under a section TextBlock, so it composes as
    // a FLAT list of leaf descriptors into a host panel — the composer renders each
    // as a top-level card, exactly the shape it had. The header/description keys are
    // the same x:Uid the hand-authored cards carried (the composer resolves them
    // from this module's .resw), so the existing copy is reused verbatim.
    //
    // SuppressRegex is a one-line free-text field; its placeholder reuses the
    // WhisperSuppressRegexBox.PlaceholderText the hand-authored TextBox carried —
    // resolved here through Loc.GetFromOptional against this module's .resw, since the
    // composed TextBox has no x:Uid of its own to auto-resolve it. Defaults read the
    // OutputFilterSettings POCO initializer, the single source SettingsService
    // persists, so each card's reset goes active exactly when the value leaves it.
>>>>>>> main
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

using System;
using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Presets;
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
    // Dictation experience — the overlay HUD group then the flat auto-paste toggle,
    // relocated from GeneralPage. The overlay is a Group (master OverlayEnabled +
    // fade/animations/position children, each masked while the master is off); the
    // position Choice matches the normalized "TopCenter"/"BottomCenter" the VM seeds.
    // The transcript panel side Choice ("Left"/"Right") sits in the same group, since the
    // panel is part of the HUD and follows its master toggle.
    // Defaults read the shell POCO initializers (OverlaySettings / PasteSettings), the
    // single source the shell's SettingsService persists. The x:Uids reuse the same
    // keys the General cards carried — already present in this module's .resw.
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
                    // Fold a possible legacy corner default through the same
                    // Top→TopCenter / else→BottomCenter rule the VM applies on Load,
                    // so the reset targets a real picker option.
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

    // GPU acceleration — the flat toggle under the "Model engine" header. It was
    // hand-authored as a lone SettingsCard with its own reset; it composes as a
    // single leaf Toggle into a host, exactly the shape it had. The default reads
    // the EngineSettings POCO initializer (the single source SettingsService
    // persists), so the composed per-card reset goes active exactly when the value
    // leaves that default. The x:Uid is the same the hand-authored card carried, so
    // the composer resolves the identical Header/Description from this module's
    // .resw. UseGpu is restart-coupled, but the RESTART FOOTER stays bespoke: it
    // watches VM.UseGpu directly, and the composer drives that same VM property, so
    // a composed toggle still trips the footer.
    public IReadOnlyList<SettingDescriptor> UseGpuSettingsManifest =>
    [
        Setting.Toggle("WhisperUseGpuCard",
            () => UseGpu,
            value => UseGpu = value,
            glyph: Glyphs.Gpu,
            defaultValue: () => new EngineSettings().UseGpu),
    ];

    // Models directory — the editable folder path that was hand-authored as a
    // FolderPickerEditableCard nested in the Model expander, with a RightContent
    // reset and a code-behind-set DefaultPath. It composes as a single Path leaf
    // with FolderPickerMode.Editable: the composer's own Path reset replaces the
    // hand-authored RightContent reset + its hover wiring, and PathArgs.DefaultPath
    // carries the deferred AppPaths lookup the code-behind used to set imperatively
    // (resolved once at compose time). The default value is the TranscriptionSettings
    // POCO initializer ("" = fall back to AppPaths.ModelsDirectory), so the reset
    // goes active exactly when the user has repointed the folder. The x:Uid reuses
    // the hand-authored WhisperModelsDirCard, so the Header/Description resolve from
    // this module's .resw unchanged.
    public IReadOnlyList<SettingDescriptor> ModelsDirectorySettingsManifest =>
    [
        Setting.Path("WhisperModelsDirCard",
            () => ModelsDirectory,
            value => ModelsDirectory = value,
            new PathArgs(FolderPickerMode.Editable, DefaultPath: () => AppPaths.ModelsDirectory),
            glyph: Glyphs.Folder,
            defaultValue: () => new TranscriptionSettings().ModelsDirectory),
    ];

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

    // Streaming pipeline — the energy segmenter. The switch is StreamingEnabled
    // (the user-facing on/off projected onto PipelineStrategyKind), a plain card
    // first and alone (settings design system, decision 10); everything under it
    // hides while streaming is off. In order: the silence threshold, the cut mode
    // fold, then the cut margin and the minimum utterance. The numeric cards are
    // field-only magnitudes (exact figures typed, not swept), their bounds copied
    // verbatim from the hand-authored NumberBoxes; the grain derives from each
    // range.
    //
    // The cut mode fold (decision 12) carries the preset selector in its header
    // and the hand values as its Custom-only children. A cut preset (Patient /
    // Balanced / Quick, see CutPresets) carries the eight hangover values: the
    // four anchors (max, min, ramp start, ramp end) and the four curve control
    // points. It does not carry the threshold (mic-dependent) nor the margin and
    // minimum utterance (blip rules). The selection is stored
    // (EnergySegmenterSettings.CutPreset), and so are the user's own values
    // (CutCustomValues): any hand edit of the eight values switches the selector
    // to Custom, and choosing Custom again writes those kept values back. Custom
    // alone reveals the four anchor cards and the curve editor; the other modes
    // show the fold's information card. The fold's reset writes the children's
    // defaults first, then returns the selector to CutPresets.Default (Balanced),
    // which equals the shipped EnergySegmenterSettings defaults.
    //
    // The switch's default mirrors the VM's own projection of the segmenter Strategy
    // onto the bool (Load/Push: StreamingEnabled = Strategy == Streaming), read off a
    // fresh StreamingSettings so the shipped default Strategy (Monolithic → false)
    // is the single source — no hand-copied bool.
    public IReadOnlyList<SettingDescriptor> StreamingSwitch =>
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
                // The instrument last and full width (decision 12): the shared
                // curve block (decision 21) over the four control points, drawn in
                // the frame the four anchor cards above set, with the sentence that
                // says what the curve does as its description.
                Setting.Curve("WhisperCutCurveCard",
                    () => new CurvePoints(SegCurveX1, SegCurveY1, SegCurveX2, SegCurveY2),
                    p => { SegCurveX1 = p.X1; SegCurveY1 = p.Y1; SegCurveX2 = p.X2; SegCurveY2 = p.Y2; },
                    new CurveArgs(Frame: CutCurveFrame, Describe: _ => CutCurveSentence()),
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

    // Context and segmentation — the two flat cards under the "Context and
    // segmentation" section header. Same flat-list shape as the output filters:
    // independent cards under a section TextBlock, composed as leaf descriptors into
    // a host. UseContext is a Toggle; MaxTokens is a field-only magnitude over the
    // -1..448 range the hand-authored NumberBox carried (the VM's
    // OnMaxTokensChanged NaN-guard is the same guard the composer applies, so a
    // cleared field never persists). Defaults read the ContextSettings
    // POCO initializer; the header/description keys match the hand-authored x:Uids.
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

    // Decoding — the master-less fold that was hand-authored as a SettingsExpander
    // with no toggle (WhisperDecodingExpander), now a Section: a header+chevron
    // grouping whose children are composed cards, with no master to gate them.
    // UseBeamSearch and BeamSize were runtime-only until now — surfaced as VM
    // properties above and exposed here; BeamSize is hidden by its VisibleWhen while
    // beam search is off (mask, never grey), its 1..10 range a sensible default
    // (the hand-authored UI never exposed it). Temperature/TemperatureIncrement are
    // magnitudes over the verbatim 0..1 bounds the hand-authored sliders carried.
    // TemperatureIncrement carries the EXISTING fallback-disabled
    // warning as an Advisory: the composer renders it as a flat note row under the
    // card when the step is 0, reusing the WhisperTemperatureIncrementWarning copy
    // (the former standalone InfoBar). All defaults read the DecodingSettings POCO
    // initializer — the single source SettingsService persists. The section's
    // header/description reuse the hand-authored WhisperDecodingExpander x:Uid.
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

    // Confidence thresholds — the second master-less fold (WhisperConfidenceExpander),
    // now a Section. The three thresholds are magnitudes; the Min/Max the
    // code-behind set imperatively to dodge the WinUI XAML-trimming parser crash
    // (Minimum > defaultValue in XAML) move INTO MagnitudeArgs here, where a
    // code-built control sets them without that parser ever seeing them — so the
    // workaround retires. Bounds verbatim: Entropy 1.5..3.5; Logprob -1.5..-0.4;
    // NoSpeech 0.05..0.80. Defaults read the ConfidenceSettings POCO initializer;
    // the section's header/description reuse the WhisperConfidenceExpander x:Uid.
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

    // Diagnostics — the dictation-scoped observability opt-ins relocated from the
    // shared Diagnostics page, in on-screen order: the streaming-transcription log
    // filter, the latency telemetry toggle, then the audio-corpus consent fold.
    //
    // Neither the log toggle nor the latency toggle carries a defaultValue: a
    // privacy/observability opt-in has no per-row "resettable default" affordance,
    // so the composer renders no per-card reset wheel — the same posture the
    // Diagnostics page's cards had.
    //
    // The corpus fold is the Setting.Group copied from DiagnosticsViewModel.CorpusSettings,
    // but its consent dialogs now ride the Catalog registry (TelemetryConsent.RequestCorpus
    // on the master, .RequestAudioCorpus on the audio child) rather than the shell's
    // ContentDialog types — so this module gates its enables behind consent without
    // referencing the shell. The master → record → content chain masks (never greys)
    // via the child radio's VisibleWhen on RecordAudioCorpus. No defaultValue anywhere:
    // a privacy opt-in carries no per-row reset.
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

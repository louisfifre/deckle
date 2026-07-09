using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
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

    // File-transcription output folder — the destination where a transcribed
    // audio file's .txt lands. Composes as a single Path leaf like the models
    // directory above, but FolderPickerMode.Configure rather than Editable: the
    // user repoints by BROWSING to a destination (and the card's Open button
    // reaches the saved transcripts), not by pasting a path carried from another
    // machine. PathArgs.DefaultPath surfaces the resolved empty-value readout —
    // the user's Desktop — computed once at compose time. The default value is the
    // TranscriptionSettings POCO initializer ("" = the Desktop sentinel), so the
    // reset goes active exactly when the user has repointed the folder and rewrites
    // the sentinel, never a resolved literal. The x:Uid resolves the card's
    // Header/Description from this module's .resw.
    public IReadOnlyList<SettingDescriptor> FileTranscriptionSettingsManifest =>
    [
        Setting.Path("WhisperFileTranscriptionDirCard",
            () => FileTranscriptionOutputDirectory,
            value => FileTranscriptionOutputDirectory = value,
            new PathArgs(FolderPickerMode.Configure,
                DefaultPath: () => TranscriptionSettingsService.ResolveFileTranscriptionOutputDirectory("")),
            glyph: Glyphs.Folder,
            defaultValue: () => new TranscriptionSettings().FileTranscriptionOutputDirectory),
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
    // a host. UseContext is a Toggle; MaxTokens is a Number whose -1..448 range and
    // 1/10 nudges are copied verbatim from the hand-authored NumberBox (the VM's
    // OnMaxTokensChanged NaN-guard is the same guard the composer's BuildNumber
    // applies, so a cleared field never persists). Defaults read the ContextSettings
    // POCO initializer; the header/description keys match the hand-authored x:Uids.
    public IReadOnlyList<SettingDescriptor> ContextSettingsManifest =>
    [
        Setting.Toggle("WhisperUseContextCard",
            () => UseContext,
            value => UseContext = value,
            glyph: Glyphs.Context,
            defaultValue: () => new ContextSettings().UseContext),
        Setting.Number("WhisperMaxTokensCard",
            () => MaxTokens,
            value => MaxTokens = value,
            new NumberArgs(-1, 448, 1, 10),
            glyph: Glyphs.Tokens,
            defaultValue: () => new ContextSettings().MaxTokens),
    ];

    // Decoding — the master-less fold that was hand-authored as a SettingsExpander
    // with no toggle (WhisperDecodingExpander), now a Section: a header+chevron
    // grouping whose children are composed cards, with no master to gate them.
    // UseBeamSearch and BeamSize were runtime-only until now — surfaced as VM
    // properties above and exposed here; BeamSize is hidden by its VisibleWhen while
    // beam search is off (mask, never grey), its 1..10 range a sensible default
    // (the hand-authored UI never exposed it). Temperature/TemperatureIncrement keep
    // the Slider kind and the verbatim 0..1 / 0.1-step bounds the hand-authored
    // sliders carried. TemperatureIncrement carries the EXISTING fallback-disabled
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
                Setting.Number("WhisperBeamSizeCard",
                    () => BeamSize,
                    value => BeamSize = value,
                    new NumberArgs(1, 10, 1, 1),
                    visibleWhen: () => UseBeamSearch,
                    defaultValue: () => new DecodingSettings().BeamSize),
                Setting.Slider("WhisperTemperatureCard",
                    () => Temperature,
                    value => Temperature = value,
                    new SliderArgs(0, 1, 0.1),
                    defaultValue: () => new DecodingSettings().Temperature),
                Setting.Slider("WhisperTemperatureIncrementCard",
                    () => TemperatureIncrement,
                    value => TemperatureIncrement = value,
                    new SliderArgs(0, 1, 0.1),
                    defaultValue: () => new DecodingSettings().TemperatureIncrement,
                    advisory: () => TemperatureIncrement == 0
                        ? Loc.GetFromOptional("Deckle.Transcription", "WhisperTemperatureIncrementWarning/Message")
                        : null),
            ],
            glyph: Glyphs.Tuning),
    ];

    // Confidence thresholds — the second master-less fold (WhisperConfidenceExpander),
    // now a Section. The three thresholds keep the Slider kind; the Min/Max/step the
    // code-behind set imperatively to dodge the WinUI XAML-trimming parser crash
    // (Minimum > defaultValue in XAML) move INTO SliderArgs here, where a code-built
    // control sets them without that parser ever seeing them — so the workaround
    // retires. Bounds verbatim: Entropy 1.5..3.5 step 0.1; Logprob -1.5..-0.4 step
    // 0.05; NoSpeech 0.05..0.80 step 0.05. Defaults read the ConfidenceSettings POCO
    // initializer; the section's header/description reuse the WhisperConfidenceExpander
    // x:Uid.
    public IReadOnlyList<SettingDescriptor> ConfidenceSettingsManifest =>
    [
        Setting.Section("WhisperConfidenceExpander",
            [
                Setting.Slider("WhisperEntropyCard",
                    () => EntropyThreshold,
                    value => EntropyThreshold = value,
                    new SliderArgs(1.5, 3.5, 0.1),
                    defaultValue: () => new ConfidenceSettings().EntropyThreshold),
                Setting.Slider("WhisperLogprobCard",
                    () => LogprobThreshold,
                    value => LogprobThreshold = value,
                    new SliderArgs(-1.5, -0.4, 0.05),
                    defaultValue: () => new ConfidenceSettings().LogprobThreshold),
                Setting.Slider("WhisperNoSpeechCard",
                    () => NoSpeechThreshold,
                    value => NoSpeechThreshold = value,
                    new SliderArgs(0.05, 0.80, 0.05),
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
        Setting.Toggle("LoggingStreamingCard",
            () => LogStreamingTranscriptionActivity,
            value => LogStreamingTranscriptionActivity = value,
            glyph: Glyphs.Speech),
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

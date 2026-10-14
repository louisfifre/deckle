using System;
using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Settings;

namespace Deckle.Transcription;

// ── WhisperViewModel — settings manifest ──────────────────────────────────────
//
// The declarative half of the module's three pages (General, Decoding,
// Streaming), kept beside the ViewModel that owns the values rather than in the
// page code-behind. Each entry declares one setting — its kind, its
// localization key (the x:Uid the hand-authored card carried, so the composer
// resolves the identical Header and Description from this module's .resw) and
// typed selectors onto this VM's own properties — and SettingsComposer turns
// each list into cards. No glyph on any card: the rail carries the module's
// icon, a card carries its words (2026-10-14).
//
// The flat cards with bespoke chrome (Language, Model with its AutoSuggestBox,
// the initial prompt, the restart footer) stay hand-authored on WhisperPage.
//
// Single-source defaults on EVERY descriptor read the POCO initializer (new
// SpeechTrimSettings().<Field>, new EnergySegmenterSettings().<Field>) — the
// same literal SettingsService persists — so each card's reset goes active
// exactly when the value leaves that default. The Seg* fields are int in the
// POCO; the int→double widening into the Func<double> default is implicit.
public partial class WhisperViewModel
{
    // Dictation experience — the overlay HUD group then the flat auto-paste toggle,
    // relocated from GeneralPage. The overlay is a Group (master OverlayEnabled +
    // fade/position children, each masked while the master is off); the
    // position Choice matches the normalized "TopCenter"/"BottomCenter" the VM seeds.
    // The overlay's own animations toggle (OverlayAnimations) is no longer exposed:
    // Windows already has one animation preference, and a second one here was a
    // setting nobody turns off (triage of 2026-10-14).
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
            defaultValue: () => new OverlaySettings().Enabled),

        Setting.Toggle("GeneralAutoPasteCard",
            () => AutoPasteEnabled,
            value => AutoPasteEnabled = value,
            defaultValue: () => new PasteSettings().AutoPasteEnabled),
    ];

    // GPU acceleration — the flat toggle under the "Model & engine" header. The
    // default reads the EngineSettings POCO initializer. UseGpu is
    // restart-coupled, but the RESTART FOOTER stays bespoke: it watches VM.UseGpu
    // directly, and the composer drives that same VM property, so a composed
    // toggle still trips the footer.
    public IReadOnlyList<SettingDescriptor> UseGpuSettingsManifest =>
    [
        Setting.Toggle("WhisperUseGpuCard",
            () => UseGpu,
            value => UseGpu = value,
            defaultValue: () => new EngineSettings().UseGpu),
    ];

    // Models directory — a Path leaf with FolderPickerMode.Editable; PathArgs
    // .DefaultPath carries the deferred AppPaths lookup (resolved once at compose
    // time). The default value is the TranscriptionSettings POCO initializer ("" =
    // fall back to AppPaths.ModelsDirectory), so the reset goes active exactly
    // when the user has repointed the folder.
    public IReadOnlyList<SettingDescriptor> ModelsDirectorySettingsManifest =>
    [
        Setting.Path("WhisperModelsDirCard",
            () => ModelsDirectory,
            value => ModelsDirectory = value,
            new PathArgs(FolderPickerMode.Editable, DefaultPath: () => AppPaths.ModelsDirectory),
            defaultValue: () => new TranscriptionSettings().ModelsDirectory),
    ];

    // Voice activity detection — the Silero pre-trim fold. The master is the
    // VadEnabled toggle; the four detection parameters are its children, hidden by
    // the composer while the master is off (it composes the master into each
    // child's VisibleWhen — native masking, no per-child gate here). The bounds
    // are the former hand-authored sliders'.
    //
    // Back on the page after the 2026-10-14 triage removed them: the Silero trim
    // is the second detector of the streaming chain, not a detail of the
    // segmenter. The energy segmenter cuts the live stream on volume alone; the
    // neural VAD then keeps the speech inside each finished utterance and drops
    // an utterance with none (docs/transcription-audio-chain.md, "Why two
    // detectors"). Its threshold and paddings decide what reaches the model, so
    // they are the module's own parameters, exposed with the switch.
    //
    // The whole fold gates on StreamingEnabled at the GROUP's visibleWhen: the
    // Silero trim runs only on the streaming path, so the entire expander collapses
    // when streaming is off.
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
                    new MagnitudeArgs(0, 1000, Unit: "ms", Integer: true),
                    defaultValue: () => new SpeechTrimSettings().MinSpeechDurationMs),
                Setting.Magnitude("WhisperVadMinSilenceCard",
                    () => VadMinSilenceDurationMs,
                    value => VadMinSilenceDurationMs = value,
                    new MagnitudeArgs(0, 1000, Unit: "ms", Integer: true),
                    defaultValue: () => new SpeechTrimSettings().MinSilenceDurationMs),
                Setting.Magnitude("WhisperVadSpeechPadCard",
                    () => VadSpeechPadMs,
                    value => VadSpeechPadMs = value,
                    new MagnitudeArgs(0, 200, Unit: "ms", Integer: true),
                    defaultValue: () => new SpeechTrimSettings().SpeechPadMs),
            ],
            visibleWhen: () => StreamingEnabled,
            defaultValue: () => new SpeechTrimSettings().Enabled),
    ];

    // Streaming pipeline — the energy segmenter. The switch is StreamingEnabled
    // (the user-facing on/off projected onto PipelineStrategyKind), a plain card
    // first and alone (settings design system, decision 10); everything under it
    // hides while streaming is off.
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
            defaultValue: () => new StreamingSettings().Strategy == PipelineStrategyKind.Streaming),
    ];

    // The segmentation cards of the Streaming page, under the switch and the
    // speech-filtering fold (decision 16): the silence threshold, the one value
    // that depends on the microphone, as a card; then the cut curve, the
    // segmentation's instrument, last and at full width, with no fold around it
    // (2026-10-14). The four hangover anchors (max, min, ramp start, ramp end)
    // are the curve's frame and live in its legend, beside the plot, as fields:
    // the doctrine's curve exception puts a curve's values in fields beside the
    // drawing (decisions 21 and 25). They keep the bounds the former
    // cards had, in the view model's milliseconds; the plot reads seconds.
    //
    // The preset selector (Patient / Balanced / Quick / Custom) is set aside
    // with every preset (decision 22): the family still runs underneath
    // (CutPreset, CutPresetStore), so a hand edit still records Custom, but
    // nothing on the page selects a preset. The cut margin and the minimum
    // utterance, two blip rules nobody tunes, keep their EnergySegmenterSettings
    // defaults and stay in the Playground only.
    public IReadOnlyList<SettingDescriptor> SegmentationSettings =>
    [
        Setting.Magnitude("WhisperSegThresholdCard",
            () => SegThresholdDbfs,
            value => SegThresholdDbfs = value,
            new MagnitudeArgs(-90, 0, Unit: "dBFS", Slider: false),
            visibleWhen: () => StreamingEnabled,
            defaultValue: () => new EnergySegmenterSettings().ThresholdDbfs),
        Setting.Curve("WhisperCutCurveCard",
            () => new CurvePoints(SegCurveX1, SegCurveY1, SegCurveX2, SegCurveY2),
            p => { SegCurveX1 = p.X1; SegCurveY1 = p.Y1; SegCurveX2 = p.X2; SegCurveY2 = p.Y2; },
            new CurveArgs(
                Frame: CutCurveFrame,
                Describe: _ => CutCurveSentence(),
                Anchors:
                [
                    new CurveAnchor("WhisperSegHangoverMaxCard",
                        () => SegHangoverMaxMs, value => SegHangoverMaxMs = value,
                        500, 15000, Integer: true,
                        Default: () => new EnergySegmenterSettings().HangoverMaxMs),
                    new CurveAnchor("WhisperSegHangoverMinCard",
                        () => SegHangoverMinMs, value => SegHangoverMinMs = value,
                        100, 2000, Integer: true,
                        Default: () => new EnergySegmenterSettings().HangoverMinMs),
                    new CurveAnchor("WhisperSegHangoverRampStartCard",
                        () => SegHangoverRampStartMs, value => SegHangoverRampStartMs = value,
                        0, 600000, Integer: true,
                        Default: () => new EnergySegmenterSettings().HangoverRampStartMs),
                    new CurveAnchor("WhisperSegHangoverRampEndCard",
                        () => SegHangoverRampEndMs, value => SegHangoverRampEndMs = value,
                        30000, 900000, Integer: true,
                        Default: () => new EnergySegmenterSettings().HangoverRampEndMs),
                ]),
            visibleWhen: () => StreamingEnabled,
            defaultValue: () =>
            {
                var d = new EnergySegmenterSettings();
                return new CurvePoints(d.HangoverCurveX1, d.HangoverCurveY1, d.HangoverCurveX2, d.HangoverCurveY2);
            }),
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

    // Decoding — four flat cards under the page's "Decoding" section: beam
    // search, its size (hidden by its VisibleWhen while beam search is off:
    // mask, never grey), the temperature and the fallback step. No fold: every
    // card of the page is always shown, so a fold would only hide what the page
    // is for (2026-10-14). TemperatureIncrement carries the fallback-disabled
    // warning as an Advisory, rendered as a note row under the card when the
    // step is 0. All defaults read the DecodingSettings POCO initializer.
    //
    // Fields alone on this page, no slider: these are figures one types after
    // reading, nothing moves on screen while they change, unlike the Ambient
    // tuning where the light answers the thumb (2026-10-14).
    public IReadOnlyList<SettingDescriptor> DecodingSettingsManifest =>
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
            new MagnitudeArgs(0, 1, Slider: false),
            defaultValue: () => new DecodingSettings().Temperature),
        Setting.Magnitude("WhisperTemperatureIncrementCard",
            () => TemperatureIncrement,
            value => TemperatureIncrement = value,
            new MagnitudeArgs(0, 1, Slider: false),
            defaultValue: () => new DecodingSettings().TemperatureIncrement,
            advisory: () => TemperatureIncrement == 0
                ? Loc.GetFromOptional("Deckle.Transcription", "WhisperTemperatureIncrementWarning/Message")
                : null),
    ];

    // Confidence — three flat cards under the page's "Confidence" section. The
    // Min/Max live in MagnitudeArgs, where a code-built control sets them without
    // the WinUI XAML parser ever seeing a Minimum above a default. Bounds
    // verbatim: Entropy 1.5..3.5; Logprob -1.5..-0.4; NoSpeech 0.05..0.80.
    // Defaults read the ConfidenceSettings POCO initializer. Fields alone, as
    // in the Decoding manifest above.
    public IReadOnlyList<SettingDescriptor> ConfidenceSettingsManifest =>
    [
        Setting.Magnitude("WhisperEntropyCard",
            () => EntropyThreshold,
            value => EntropyThreshold = value,
            new MagnitudeArgs(1.5, 3.5, Slider: false),
            defaultValue: () => new ConfidenceSettings().EntropyThreshold),
        Setting.Magnitude("WhisperLogprobCard",
            () => LogprobThreshold,
            value => LogprobThreshold = value,
            new MagnitudeArgs(-1.5, -0.4, Slider: false),
            defaultValue: () => new ConfidenceSettings().LogprobThreshold),
        Setting.Magnitude("WhisperNoSpeechCard",
            () => NoSpeechThreshold,
            value => NoSpeechThreshold = value,
            new MagnitudeArgs(0.05, 0.80, Slider: false),
            defaultValue: () => new ConfidenceSettings().NoSpeechThreshold),
    ];

    // Output filters — three flat cards under the page's "Output filters"
    // section, after Decoding and Confidence: what the model's text loses before
    // it is pasted. Not decoding, so not under that title; still the module's
    // own, so still on the page (2026-10-14). SuppressRegex is a one-line
    // free-text field; its placeholder reuses the WhisperSuppressRegexBox
    // .PlaceholderText entry. Defaults read the OutputFilterSettings POCO
    // initializer.
    public IReadOnlyList<SettingDescriptor> OutputFilterSettingsManifest =>
    [
        Setting.Toggle("WhisperSuppressNstCard",
            () => SuppressNonSpeechTokens,
            value => SuppressNonSpeechTokens = value,
            defaultValue: () => new OutputFilterSettings().SuppressNonSpeechTokens),
        Setting.Toggle("WhisperSuppressBlankCard",
            () => SuppressBlank,
            value => SuppressBlank = value,
            defaultValue: () => new OutputFilterSettings().SuppressBlank),
        Setting.Text("WhisperSuppressRegexCard",
            () => SuppressRegex,
            value => SuppressRegex = value,
            new TextArgs(Placeholder: Loc.GetFromOptional("Deckle.Transcription", "WhisperSuppressRegexBox/PlaceholderText")),
            defaultValue: () => new OutputFilterSettings().SuppressRegex),
    ];

    // Context — two flat cards under the page's "Context" section, last:
    // whether each segment primes the next and how much text a segment may
    // produce. UseContext is a Toggle; MaxTokens is a field-only magnitude over
    // the -1..448 range (the VM's OnMaxTokensChanged NaN-guard is the same guard
    // the composer applies, so a cleared field never persists). Defaults read
    // the ContextSettings POCO initializer.
    public IReadOnlyList<SettingDescriptor> ContextSettingsManifest =>
    [
        Setting.Toggle("WhisperUseContextCard",
            () => UseContext,
            value => UseContext = value,
            defaultValue: () => new ContextSettings().UseContext),
        Setting.Magnitude("WhisperMaxTokensCard",
            () => MaxTokens,
            value => MaxTokens = value,
            new MagnitudeArgs(-1, 448, Slider: false, Integer: true),
            defaultValue: () => new ContextSettings().MaxTokens),
    ];
}

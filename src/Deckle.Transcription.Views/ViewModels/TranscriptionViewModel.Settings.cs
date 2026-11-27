using System;
using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Settings;
using Deckle.Localization;

namespace Deckle.Transcription.Views;

// ── TranscriptionViewModel — settings manifest ──────────────────────────────────────
//
// The declarative half of the module's pages (General, Lexicon),
// kept beside the ViewModel that owns the values rather than in the page
// code-behind. Each entry declares one setting — its kind, its
// localization key (the x:Uid the hand-authored card carried, so the composer
// resolves the identical Header and Description from this module's .resw) and
// typed selectors onto this VM's own properties — and SettingsComposer turns
// each list into cards. No glyph on any card: the rail carries the module's
// icon, a card carries its words (2026-10-14).
//
// The restart footer stays hand-authored on TranscriptionPage.
//
// Single-source defaults on EVERY descriptor read the POCO initializer (new
// SpeechTrimSettings().<Field>, new EnergySegmenterSettings().<Field>) — the
// same literal SettingsService persists — so each card's reset goes active
// exactly when the value leaves that default. The Seg* fields are int in the
// POCO; the int→double widening into the Func<double> default is implicit.
public partial class TranscriptionViewModel
{
    // Dictation experience — the overlay HUD group, relocated from
    // GeneralPage. The overlay is a Group (master OverlayEnabled +
    // fade/position children, each masked while the master is off); the
    // position Choice matches the normalized "TopCenter"/"BottomCenter" the VM seeds.
    // Overlay animations are not set here: they follow the app-wide Animations
    // setting on the General page (Deckle.Hud.AnimationPreference, ADR-0009).
    // The transcript panel side Choice ("Left"/"Right") sits in the same group, since the
    // panel is part of the HUD and follows its master toggle.
    // Defaults read the shell POCO initializer (OverlaySettings), the
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
                    defaultValue: () => new OverlaySettings().Position),
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
    ];

    // Maximum recording length — a group: the master switch "Limit recording
    // duration", off by default (the POCO's 0 = no limit), reveals one length in
    // minutes, a field with a slider (a length is swept by feel). The POCO and
    // the capture loop keep seconds; RecordingLimit and the two VM accessors do
    // the conversion. The child's default is the length the switch restores, so
    // the section reset returns the group to off. Hosted here, stored in the
    // capture settings (TranscriptionViewModel.RecordingLimit.cs).
    public IReadOnlyList<SettingDescriptor> RecordingLimitSettings =>
    [
        Setting.Group("RecordingLimitCard",
            () => LimitRecordingDuration,
            value => LimitRecordingDuration = value,
            [
                Setting.Magnitude("RecordingLimitDurationCard",
                    () => MaxRecordingDurationMinutes,
                    value => MaxRecordingDurationMinutes = value,
                    new MagnitudeArgs(Deckle.Audio.RecordingLimit.MinMinutes, Deckle.Audio.RecordingLimit.MaxMinutes,
                        Unit: "min", Integer: true),
                    defaultValue: () => Deckle.Audio.RecordingLimit.DefaultMinutes),
            ],
            defaultValue: () => Deckle.Audio.RecordingLimit.IsLimited(new Deckle.Audio.CaptureSettings().MaxRecordingDurationSeconds)),
    ];

    // GPU acceleration — the flat toggle under the "Speech engine" header. The
    // default reads the EngineSettings POCO initializer. UseGpu is
    // restart-coupled, but the RESTART FOOTER stays bespoke: it watches VM.UseGpu
    // directly, and the composer drives that same VM property, so a composed
    // toggle still trips the footer.
    public IReadOnlyList<SettingDescriptor> UseGpuSettingsManifest =>
    [
        Setting.Toggle("TranscriptionUseGpuCard",
            () => UseGpu,
            value => UseGpu = value,
            defaultValue: () => new EngineSettings().UseGpu),
    ];

    // Models folder — a standalone Path, so the composer renders it as the
    // folder fold ("Models folder" and its sentence in the header, the Location
    // line inside), with FolderPickerMode.Editable: a typeable field in the
    // path's place, for a path pasted from another machine. PathArgs
    // .DefaultPath carries the deferred AppPaths lookup (resolved once at compose
    // time). The default value is the TranscriptionSettings POCO initializer ("" =
    // fall back to AppPaths.ModelsDirectory), so the reset goes active exactly
    // when the user has repointed the folder.
    public IReadOnlyList<SettingDescriptor> ModelsDirectorySettingsManifest =>
    [
        Setting.Path("TranscriptionModelsDirCard",
            () => ModelsDirectory,
            value => ModelsDirectory = value,
            new PathArgs(FolderPickerMode.Editable, DefaultPath: () => AppPaths.ModelsDirectory, ShowValue: true),
            defaultValue: () => new TranscriptionSettings().ModelsDirectory),
    ];

    // The guard against text from silence and noise: the Silero trim, one fold
    // with no master in the Segmentation section at the foot of Dictation >
    // General (developer mode only), collapsed. Its values
    // apply to EVERY recording (the streaming dictation, the monolithic take, a
    // file, a recovered take), so the fold shows whatever the streaming switch
    // says. It stays collapsed: almost nobody moves these values.
    //
    // The Silero trim is the second detector of the streaming chain, not a
    // detail of the segmenter. The energy segmenter cuts the live stream on
    // volume alone; the neural VAD then keeps the speech inside each finished
    // utterance and drops an utterance with none
    // (docs/transcription-audio-chain.md, "Why two detectors"). Its threshold
    // and paddings decide what reaches the model, so they are the module's own
    // parameters, exposed with the switch. Its minimum speech is not among them:
    // it is the shared floor the segmenter reads too (MinSpeechMs), in the
    // Segmentation section, since it must stay visible while this fold is closed.
    // A Section, not a Group inside a fold: folds never nest, so the Silero
    // switch is a plain child and its three parameters hide while it is off
    // through their own VisibleWhen.
    //
    // The fold shows whatever the strategy: the Silero trim runs on every
    // recording, the streaming dictation's utterances, the monolithic take as
    // one utterance, and the utterances of a file or a recovered take.
    public IReadOnlyList<SettingDescriptor> SilenceGuardSettings =>
    [
        Setting.Section("TranscriptionSilenceGuardsFold",
            [
                Setting.Toggle("TranscriptionVadEnabledCard",
                    () => VadEnabled,
                    value => VadEnabled = value,
                    defaultValue: () => new SpeechTrimSettings().Enabled),
                Setting.Magnitude("TranscriptionVadThresholdCard",
                    () => VadThreshold,
                    value => VadThreshold = value,
                    new MagnitudeArgs(0.1, 0.9),
                    visibleWhen: () => VadEnabled,
                    defaultValue: () => new SpeechTrimSettings().Threshold),
                Setting.Magnitude("TranscriptionVadMinSilenceCard",
                    () => Seconds(VadMinSilenceDurationMs),
                    value => VadMinSilenceDurationMs = Milliseconds(value),
                    new MagnitudeArgs(0, 1, Unit: "s"),
                    visibleWhen: () => VadEnabled,
                    defaultValue: () => Seconds(new SpeechTrimSettings().MinSilenceDurationMs)),
                // 0..0.4 s, not 0..0.2: the grain follows the span, and 0.01 s
                // is the one that writes the 0.03 s default exactly.
                Setting.Magnitude("TranscriptionVadSpeechPadCard",
                    () => Seconds(VadSpeechPadMs),
                    value => VadSpeechPadMs = Milliseconds(value),
                    new MagnitudeArgs(0, 0.4, Unit: "s"),
                    visibleWhen: () => VadEnabled,
                    defaultValue: () => Seconds(new SpeechTrimSettings().SpeechPadMs)),
            ]),
    ];

    // Transcribe as you speak — the streaming pipeline. The switch is StreamingEnabled
    // (the user-facing on/off projected onto PipelineStrategyKind), a plain card
    // first in the "While you speak" section. It chooses the dictation pipeline
    // only: the Segmentation section stays as it is while streaming is off, since
    // files and recovered takes are always cut and trimmed with its values.
    //
    // The switch's default mirrors the VM's own projection of the segmenter Strategy
    // onto the bool (Load/Push: StreamingEnabled = Strategy == Streaming), read off a
    // fresh StreamingSettings so the shipped default Strategy (Streaming → true)
    // is the single source — no hand-copied bool.
    public IReadOnlyList<SettingDescriptor> StreamingSwitch =>
    [
        Setting.Toggle("TranscriptionStreamingEnabledCard",
            () => StreamingEnabled,
            value => StreamingEnabled = value,
            defaultValue: () => new StreamingSettings().Strategy == PipelineStrategyKind.Streaming),
    ];

    // The segmentation section of Dictation > General (developer mode only),
    // above the silence guards' fold: the cut curve, with the cut margin and
    // the minimum speech in its pause strip.
    // The minimum speech is the one floor the segmenter and Silero share
    // (Streaming.MinSpeechMs); it sits here and not in the guards' fold
    // because it is the cut's own floor, and the guard left when Silero is off
    // or its model is missing. The level that makes a frame voiced is not here: it is the voice
    // level of the Microphone page (CaptureSettings.VoiceLevelDbfs), since the HUD
    // and the low-audio warning read it too. The curve is the segmentation's
    // instrument, at full width, with no fold around it (2026-10-14). Every
    // value shows whatever the strategy: files and recovered takes are always
    // cut. The four hangover anchors (max, min, ramp start, ramp end) are the
    // curve's frame and keep the bounds the former cards had, in the view
    // model's milliseconds; the plot reads seconds.
    //
    // Since 2026-11-07 the curve card is the whole section, edited by direct
    // manipulation: each anchor's Role binds it to the frame coordinate it is,
    // so the curve's two corners drag the ramp and the waits; the cut margin
    // and the minimum speech, once two cards beside the curve, are the pause
    // strip under it (CurveArgs.Pause), a pause at the scale of a word, since on
    // the curve's 140 s axis they would be under a pixel. Every value keeps its
    // field, its bounds, its default and its description, under the card's one
    // reset.
    public IReadOnlyList<SettingDescriptor> SegmentationSettings =>
    [
        Setting.Curve("TranscriptionCutCurveCard",
            () => new CurvePoints(SegCurveX1, SegCurveY1, SegCurveX2, SegCurveY2),
            p => { SegCurveX1 = p.X1; SegCurveY1 = p.Y1; SegCurveX2 = p.X2; SegCurveY2 = p.Y2; },
            new CurveArgs(
                Frame: CutCurveFrame,
                Describe: _ => CutCurveSentence(),
                Anchors:
                [
                    new CurveAnchor("TranscriptionSegHangoverMaxCard",
                        () => Seconds(SegHangoverMaxMs), value => SegHangoverMaxMs = Milliseconds(value),
                        0.5, 15,
                        Default: () => Seconds(new EnergySegmenterSettings().HangoverMaxMs),
                        Role: CurveAnchorRole.OriginY),
                    new CurveAnchor("TranscriptionSegHangoverMinCard",
                        () => Seconds(SegHangoverMinMs), value => SegHangoverMinMs = Milliseconds(value),
                        0.1, 2,
                        Default: () => Seconds(new EnergySegmenterSettings().HangoverMinMs),
                        Role: CurveAnchorRole.EndY),
                    new CurveAnchor("TranscriptionSegHangoverRampStartCard",
                        () => Seconds(SegHangoverRampStartMs), value => SegHangoverRampStartMs = Milliseconds(value),
                        0, 200, Integer: true, // a 200 s span gives the 5 s grain
                        Default: () => Seconds(new EnergySegmenterSettings().HangoverRampStartMs),
                        Role: CurveAnchorRole.OriginX),
                    new CurveAnchor("TranscriptionSegHangoverRampEndCard",
                        () => Seconds(SegHangoverRampEndMs), value => SegHangoverRampEndMs = Milliseconds(value),
                        30, 230, Integer: true, // same 200 s span, same 5 s grain
                        Default: () => Seconds(new EnergySegmenterSettings().HangoverRampEndMs),
                        Role: CurveAnchorRole.EndX),
                ],
                Pause: new PauseStripArgs("TranscriptionCutPauseStrip",
                    // 0..1 s: the grain is 0.05 s, which writes both defaults
                    // (0.25, 0.15) exactly.
                    MinimumSpeech: new CurveAnchor("TranscriptionMinSpeechCard",
                        () => Seconds(MinSpeechMs), value => MinSpeechMs = Milliseconds(value),
                        0, 1,
                        Default: () => Seconds(new StreamingSettings().MinSpeechMs)),
                    Margin: new CurveAnchor("TranscriptionSegMarginCard",
                        () => Seconds(SegMarginMs), value => SegMarginMs = Milliseconds(value),
                        0, 1,
                        Default: () => Seconds(new EnergySegmenterSettings().MarginMs)),
                    // The shortest pause that cuts: the minimum wait, where a
                    // long utterance ends.
                    CutAfter: () => Seconds(SegHangoverMinMs))),
            defaultValue: () =>
            {
                var d = new EnergySegmenterSettings();
                return new CurvePoints(d.HangoverCurveX1, d.HangoverCurveY1, d.HangoverCurveX2, d.HangoverCurveY2);
            }),
    ];

    // The durations are stored and held in milliseconds (the settings file, the
    // POCOs and the view model keep their unit, so a saved file loads unchanged)
    // and shown in seconds: the manifest converts at the selector, the one place
    // the unit changes. The milliseconds are rounded back to whole ones, so a
    // field value of 0.03 s lands on 30, never 29.999.
    private static double Seconds(double milliseconds) => milliseconds / 1000.0;
    private static double Milliseconds(double seconds) => Math.Round(seconds * 1000.0);

    // The frame of the cut curve: X is the open-utterance length in seconds, from
    // 0 to a little past the ramp end; Y is the decision delay in seconds, from 0
    // to a quarter above the maximum hangover. The unit square runs from (ramp
    // start, max) down to (ramp end, min), the decay the segmenter eases
    // between. The view model keeps the anchors in milliseconds, the plot reads
    // seconds. The room above the maximum and past the ramp end is where the
    // curve's corners can be dragged: the plot's scale holds still during a
    // drag and follows the new values at the release (2026-11-07).
    private CurveFrame CutCurveFrame()
    {
        double max = Math.Max(SegHangoverMaxMs / 1000.0, 1e-3);
        double min = Math.Min(SegHangoverMinMs / 1000.0, max);
        double start = Math.Max(0, SegHangoverRampStartMs / 1000.0);
        double end = Math.Max(start, SegHangoverRampEndMs / 1000.0);
        double xMax = end + Math.Max((end - start) * 0.15, 5.0);
        double yMax = max * 1.25;
        return new CurveFrame(start, max, end, min, xMax, yMax, Axes: true, XUnit: "s", YUnit: "s");
    }

    // The plain-language reading of the envelope the curve eases. Its words
    // live in the module's .resw like every other string of the page; a
    // missing entry shows no sentence.
    private string? CutCurveSentence()
    {
        string? format = Loc.GetFromOptional("Deckle.Transcription.Views", "TranscriptionCutCurveSentence_Format");
        if (format is null) return null;
        double start = SegHangoverRampStartMs / 1000.0;
        double end = Math.Max(start, SegHangoverRampEndMs / 1000.0);
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, format,
            SegHangoverMaxMs / 1000.0, start, SegHangoverMinMs / 1000.0, end);
    }
}

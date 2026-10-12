using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Composition;

namespace Deckle.Playground;

// ─── HUD playground target enum ──────────────────────────────────────────────
//
// Four HUD states (Charging / Recording / Transcribing / Rewriting) and three
// underlying primitives (Conic / ArcMask / Combined) the developer might want
// to inspect in isolation. The DropDownButton in HudPage groups them under
// two sub-menus ; persistence is in-memory only so the enum doesn't need to
// be stable across releases.
public enum HudTarget
{
    Charging,
    Recording,
    Transcribing,
    Rewriting,
    Conic,
    ArcMask,
    Combined,
    // A second, freely-placed conic cone (own surface) painted from the same
    // config as the contour — a placement sandbox for the digit-reveal material.
    ConicClone,
    UtteranceCountdown,
    CountdownMaterial,
    CountdownMasks,
    MaskedCountdown,
}

// Logical groups of tuning expanders. The HUD tuning panel is rebuilt
// every time the target changes, showing only the sections whose rows
// reach the active target's preview. Parked follows them (collapsed by
// default) so the developer can tweak transition-only defaults from any
// target; it is skipped when the list is empty (Charging, no stroke).
public enum HudTuningSection
{
    Geometry,
    Palette,
    ConicFade,
    HueRotation,
    ArcRotation,
    Recording,
    Transcribing,
    Rewriting,
    SimulatedRms,
    ClonePlacement,
    UtteranceCountdown,
    Parked,
}

// ─── HUD playground ViewModel ────────────────────────────────────────────────
//
// Thin transport + target state. The actual stroke tuning lives in the
// mutable TuningModel POCO directly mutated by the slider lambdas in
// HudPage code-behind (60+ fields, programmatic Slider/NumberBox composite
// — wrapping each one as [ObservableProperty] would add ~3× the code for
// no x:Bind payoff since the panel itself is built programmatically).
//
// The ViewModel exposes only what HudPage.xaml actually binds : the
// current target, the play state, and the computed enable-flags for the
// Play / Pause / Stop transport buttons. Mirrors the GeneralViewModel
// pattern (CommunityToolkit.Mvvm partial properties, _isSyncing guard
// — here named _suppressTargetEvents for the same role) without the
// PushToSettings step since HUD tuning isn't persisted.
public partial class HudViewModel : ObservableObject
{
    // Suppresses change side-effects when the page is programmatically
    // setting the target during initialisation or during a card-click
    // mutual-exclusion sweep. The page sets this true around any
    // bulk-write block, mirrors the `_isSyncing` flag from GeneralViewModel.
    private bool _suppressTargetEvents;

    public bool SuppressTargetEvents
    {
        get => _suppressTargetEvents;
        set => _suppressTargetEvents = value;
    }

    [ObservableProperty]
    public partial HudTarget CurrentTarget { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    // Which state's grading the geometry-only previews (Conic / ArcMask /
    // Combined / Conic clone) are shown in — Transcribing (greyscale) or
    // Rewriting (full colour). Inert for the four HUD-state targets, which carry
    // their own variant. Default Rewriting so a freshly-selected cone shows in
    // colour (its placement is unreadable in flat grey).
    [ObservableProperty]
    public partial ProcessingVariant PreviewVariant { get; set; }

    public HudViewModel()
    {
        // Default target = Transcribing, mirrors the previous in-memory
        // default that the original PlaygroundWindow seeded. Pause by
        // default so the window opens to an empty preview, the user
        // clicks Play to start.
        CurrentTarget  = HudTarget.Transcribing;
        IsPlaying      = false;
        PreviewVariant = ProcessingVariant.Rewriting;
    }

    // The Transcribing/Rewriting grading toggle only makes sense for the
    // geometry-only previews; the HUD-state targets already encode their own
    // variant. HudPage binds the toggle's Visibility here.
    public bool IsVariantToggleVisible =>
        CurrentTarget is HudTarget.Conic
                      or HudTarget.Combined
                      or HudTarget.ConicClone;

    public bool IsCountdownTarget => CurrentTarget is HudTarget.UtteranceCountdown
        or HudTarget.CountdownMaterial or HudTarget.CountdownMasks or HudTarget.MaskedCountdown;

    // Computed transport enable-flags. Derived from IsPlaying ; the
    // partial change method below fires change notifications for these
    // so x:Bind picks them up on the toolbar. Two-button transport
    // (Play / Stop) ; Pause was retired in 2026-05-19 because freezing
    // the Composition Forever animations would require new entry points
    // in Deckle.Hud and Deckle.Composition, out of scope here.

    public bool IsPlayEnabled => !IsPlaying;
    public bool IsStopEnabled => IsPlaying;

    // Human-readable label shown on the DropDownButton. Same case as the
    // enum members — they read as proper UI labels by design ("Recording",
    // "Conic"…).
    // Friendly labels for the DropDownButton — the raw enum names read poorly
    // for the primitives ("ArcMask"), and these match the MenuFlyout item text.
    public string CurrentTargetLabel => CurrentTarget switch
    {
        HudTarget.Conic      => "Cone",
        HudTarget.ArcMask    => "Sweep mask",
        HudTarget.Combined   => "Masked cone",
        HudTarget.ConicClone => "Cone clone",
        HudTarget.UtteranceCountdown => "Utterance countdown",
        HudTarget.CountdownMaterial => "Countdown material",
        HudTarget.CountdownMasks => "Countdown masks",
        HudTarget.MaskedCountdown => "Masked countdown",
        _                    => CurrentTarget.ToString(),
    };

    partial void OnCurrentTargetChanged(HudTarget value)
    {
        OnPropertyChanged(nameof(CurrentTargetLabel));
        OnPropertyChanged(nameof(IsVariantToggleVisible));
    }

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPlayEnabled));
        OnPropertyChanged(nameof(IsStopEnabled));
    }

    // Forces the preview back to its idle, non-animated state — invoked
    // by the Stop button and by PlaygroundWindow.ShowAndActivate so each
    // window re-show lands on a known state regardless of where the user
    // left it.
    public void ForceStop()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
    }

    // Returns the tuning sections whose rows reach the active target's
    // preview. A generic section replaced by the target's own section is
    // hidden; its rows stay reachable from another target because the
    // page holds one shared TuningModel. HudPage's BuildTuningPanel
    // creates the matching expanders, then Parked, unless the list is
    // empty.
    public IReadOnlyCollection<HudTuningSection> ActiveTuningSections =>
        CurrentTarget switch
        {
            // Charging: HudChrono.ApplyCharging detaches the stroke and no
            // config is armed, so no row reaches the preview.
            HudTarget.Charging => System.Array.Empty<HudTuningSection>(),
            // No ConicFade nor HueRotation: CreateRecordingStroke replaces the
            // generic conic slots with the Recording* fields and overrides the
            // hue period, so those rows would change nothing. The Recording
            // expander carries the rows that act.
            HudTarget.Recording =>
                new[]
                {
                    HudTuningSection.Geometry,
                    HudTuningSection.Palette,
                    HudTuningSection.Recording,
                    HudTuningSection.SimulatedRms,
                },
            // Transcribing: CreateProcessingStroke spins the arc mask with the
            // Arc* fields (the visible sweep) and the digit reveal reads Clone*.
            HudTarget.Transcribing =>
                new[]
                {
                    HudTuningSection.Geometry,
                    HudTuningSection.Palette,
                    HudTuningSection.ConicFade,
                    HudTuningSection.HueRotation,
                    HudTuningSection.ArcRotation,
                    HudTuningSection.Transcribing,
                    HudTuningSection.ClonePlacement,
                },
            // Rewriting: same CreateProcessingStroke as Transcribing, own grading.
            HudTarget.Rewriting =>
                new[]
                {
                    HudTuningSection.Geometry,
                    HudTuningSection.Palette,
                    HudTuningSection.ConicFade,
                    HudTuningSection.HueRotation,
                    HudTuningSection.ArcRotation,
                    HudTuningSection.Rewriting,
                    HudTuningSection.ClonePlacement,
                },
            // Conic: CreateNakedMaskPreview shows only the conic brush, which
            // spins with the Hue* fields; the arc fade never shows.
            HudTarget.Conic =>
                new[]
                {
                    HudTuningSection.Geometry,
                    HudTuningSection.Palette,
                    HudTuningSection.HueRotation,
                },
            // ArcMask: CreateNakedMaskPreview shows only the arc brush, painted
            // from the ConicFade fields and spun by the Arc* fields.
            HudTarget.ArcMask =>
                new[]
                {
                    HudTuningSection.Geometry,
                    HudTuningSection.ConicFade,
                    HudTuningSection.ArcRotation,
                },
            // Countdown views: the gauge reads the shared geometry and its own
            // tuning; no stroke palette or rotation reaches the preview.
            HudTarget.UtteranceCountdown or HudTarget.CountdownMaterial
                or HudTarget.CountdownMasks or HudTarget.MaskedCountdown =>
                new[] { HudTuningSection.UtteranceCountdown },
            // ConicClone: CreateConicClonePreview ignores InsetDip and replaces
            // Lightness/Chroma and the hue period/direction with the Clone* fields.
            HudTarget.ConicClone =>
                new[]
                {
                    HudTuningSection.ConicFade,
                    HudTuningSection.ClonePlacement,
                },
            // Combined: CreateNakedMaskPreview shows the masked cone, so every
            // generic section acts.
            _ /* Combined */ =>
                new[]
                {
                    HudTuningSection.Geometry,
                    HudTuningSection.Palette,
                    HudTuningSection.ConicFade,
                    HudTuningSection.HueRotation,
                    HudTuningSection.ArcRotation,
                },
        };
}

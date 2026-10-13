using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Composition;

namespace Deckle.Playground;

// The HUD states, then the primitives to inspect in isolation. Never
// persisted, so the members need not stay stable across releases.
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

// Logical groups of tuning expanders. The panel shows only the sections whose
// rows reach the active target's preview. Parked follows them so the
// transition-only defaults stay reachable, except on the targets without a
// stroke: Charging (empty list) and the countdown views.
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

// Transport and target state only. The stroke tuning lives in the mutable
// TuningModel the slider lambdas in HudPage write directly: the panel is
// built in code, so wrapping each field as [ObservableProperty] would buy no
// x:Bind. HUD tuning is not persisted: a value that proves right becomes a
// code default, never a second source of truth.
public partial class HudViewModel : ObservableObject
{
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

    // Grading of the geometry-only previews: Transcribing (greyscale) or
    // Rewriting (full colour). The HUD-state targets carry their own variant.
    // Default Rewriting: a cone's placement is unreadable in flat grey.
    [ObservableProperty]
    public partial ProcessingVariant PreviewVariant { get; set; }

    public HudViewModel()
    {
        CurrentTarget  = HudTarget.Transcribing;
        IsPlaying      = false;
        PreviewVariant = ProcessingVariant.Rewriting;
    }

    public bool IsVariantToggleVisible =>
        CurrentTarget is HudTarget.Conic
                      or HudTarget.Combined
                      or HudTarget.ConicClone;

    public bool IsCountdownTarget => CurrentTarget is HudTarget.UtteranceCountdown
        or HudTarget.CountdownMaterial or HudTarget.CountdownMasks or HudTarget.MaskedCountdown;

    // No Pause: freezing the Composition Forever animations would need new
    // entry points in Deckle.Hud and Deckle.Composition.
    public bool IsPlayEnabled => !IsPlaying;
    public bool IsStopEnabled => IsPlaying;

    // The raw enum names read poorly for the primitives ("ArcMask"); these
    // labels match the MenuFlyout item text in HudPage.xaml.
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

    public void ForceStop()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
    }

    // The sections whose rows reach the active target's preview. A generic
    // section replaced by the target's own section is hidden; its rows stay
    // reachable from another target because the page holds one shared
    // TuningModel.
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

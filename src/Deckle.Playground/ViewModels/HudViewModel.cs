using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Composition;

namespace Deckle.Playground;

// The HUD states, then the parts of the stroke to inspect in isolation. Never
// persisted, so the members need not stay stable across releases.
public enum HudTarget
{
    Charging,
    Recording,
    Transcribing,
    Rewriting,
    Cone,
    SweepMask,
    MaskedCone,
    // The gauge the pause opens in Recording, on its own, with the cone it
    // samples shown behind it.
    Gauge,
    // The digit-reveal material on its own: a freely placed cone on its own
    // surface, painted from the same config as the contour, to tune its seat.
    DigitReveal,
}

// Logical groups of tuning expanders. The panel shows only the sections whose
// rows reach the active target's preview. Parked follows them so the
// transition-only defaults stay reachable, except on the targets without a
// stroke: Charging (empty list) and Gauge.
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
// x:Bind. The page saves that model to the HUD settings store from its
// floating bar; nothing here is stored.
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

    // The material the parts previews paint the cone with. The HUD-state
    // targets show their own state's material instead. Default Colour: a
    // cone's placement is unreadable in flat grey.
    [ObservableProperty]
    public partial HudMaterial PreviewMaterial { get; set; }

    public HudViewModel()
    {
        CurrentTarget   = HudTarget.Transcribing;
        IsPlaying       = false;
        PreviewMaterial = HudMaterial.Colour;
    }

    // Only the parts that paint the cone without a state offer the choice:
    // the sweep mask has no material and the gauge shows Recording's.
    public bool IsMaterialToggleVisible =>
        CurrentTarget is HudTarget.Cone
                      or HudTarget.MaskedCone
                      or HudTarget.DigitReveal;

    // No Pause: freezing the Composition Forever animations would need new
    // entry points in Deckle.Hud and Deckle.Composition.
    public bool IsPlayEnabled => !IsPlaying;
    public bool IsStopEnabled => IsPlaying;

    // The raw enum names read poorly for the multi-word members
    // ("SweepMask"); these labels match the MenuFlyout item text in
    // HudPage.xaml.
    public string CurrentTargetLabel => CurrentTarget switch
    {
        HudTarget.SweepMask   => "Sweep mask",
        HudTarget.MaskedCone  => "Masked cone",
        HudTarget.DigitReveal => "Digit reveal",
        _                     => CurrentTarget.ToString(),
    };

    partial void OnCurrentTargetChanged(HudTarget value)
    {
        OnPropertyChanged(nameof(CurrentTargetLabel));
        OnPropertyChanged(nameof(IsMaterialToggleVisible));
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
            // generic sweep slots with the Recording* fields and freezes the
            // hue rotation, so those rows would change nothing. The Recording
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
            // Rewriting: same CreateProcessingStroke as Transcribing, own material.
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
            // Cone: CreateNakedMaskPreview shows only the conic brush, which
            // spins with the Hue* fields; the arc fade never shows.
            HudTarget.Cone =>
                new[]
                {
                    HudTuningSection.Geometry,
                    HudTuningSection.Palette,
                    HudTuningSection.HueRotation,
                },
            // SweepMask: CreateNakedMaskPreview shows only the arc brush,
            // painted from the ConicFade fields and spun by the Arc* fields.
            HudTarget.SweepMask =>
                new[]
                {
                    HudTuningSection.Geometry,
                    HudTuningSection.ConicFade,
                    HudTuningSection.ArcRotation,
                },
            // Gauge: its own reach rows, then the Recording rows it shares —
            // fades and material (span, mirror and arc phase in that section
            // do not reach it: the two wedges follow the pause).
            HudTarget.Gauge =>
                new[] { HudTuningSection.UtteranceCountdown, HudTuningSection.Recording },
            // DigitReveal: CreateConicClonePreview ignores InsetDip and replaces
            // Lightness/Chroma and the hue period/direction with the Clone* fields.
            HudTarget.DigitReveal =>
                new[]
                {
                    HudTuningSection.ConicFade,
                    HudTuningSection.ClonePlacement,
                },
            // MaskedCone: CreateNakedMaskPreview shows the masked cone, so
            // every generic section acts.
            _ /* MaskedCone */ =>
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

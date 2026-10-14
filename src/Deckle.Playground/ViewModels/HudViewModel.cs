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
// rows reach the active target's preview, in the order ActiveTuningSections
// lists them. Simulation drives the preview only and is never saved.
public enum HudTuningSection
{
    Geometry,
    ColourMaterial,
    GreyMaterial,
    SweepMask,
    HueRotation,
    SweepRotation,
    Recording,
    Transcribing,
    Rewriting,
    Gauge,
    DigitReveal,
    Simulation,
    Orientations,
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

    // The material the active target paints its cone with. A HUD state shows
    // its own state's material, the gauge shows Recording's (it is the
    // Recording outline opened by the pause), and the cone parts show the
    // header radio's. The state materials are read from the tuning the page
    // passes in, never mirrored here: the page's TuningModel stays their one
    // source, and the Save bar compares that model alone. Charging and the
    // sweep mask paint no cone; they fall through to the radio unused.
    public HudMaterial MaterialInPlay(HudComposition.ConicArcStrokeConfig tuning) =>
        CurrentTarget switch
        {
            HudTarget.Recording or HudTarget.Gauge => tuning.RecordingMaterial,
            HudTarget.Transcribing                 => tuning.TranscribingMaterial,
            HudTarget.Rewriting                    => tuning.RewritingMaterial,
            _                                      => PreviewMaterial,
        };

    // The sections whose rows reach the active target's preview, in panel
    // order: the one map of which section shows where. It takes the page's
    // tuning because the material in play decides between the two material
    // sections and whether a hue rotation can show at all; a section replaced
    // by the target's own stays reachable from another target, since the page
    // holds one shared TuningModel.
    public IReadOnlyList<HudTuningSection> ActiveTuningSections(HudComposition.ConicArcStrokeConfig tuning)
    {
        bool colour = MaterialInPlay(tuning) == HudMaterial.Colour;
        var material = colour ? HudTuningSection.ColourMaterial : HudTuningSection.GreyMaterial;
        // On the flat grey a hue rotation changes nothing on screen.
        HudTuningSection[] hueRotation = colour ? [HudTuningSection.HueRotation] : [];

        return CurrentTarget switch
        {
            // Charging: HudChrono.ApplyCharging detaches the stroke and no
            // config is armed, so no row reaches the preview.
            HudTarget.Charging => [],
            // No SweepMask nor HueRotation: CreateRecordingStroke replaces the
            // generic sweep slots with the Recording* fields and freezes both
            // rotations, so those rows would change nothing. The Recording
            // section carries the rows that act; the gauge opens during
            // Recording, so its reach shows here too.
            HudTarget.Recording =>
            [
                HudTuningSection.Geometry, material, HudTuningSection.Recording,
                HudTuningSection.Gauge, HudTuningSection.Simulation, HudTuningSection.Orientations,
            ],
            // CreateProcessingStroke spins the cone with the Hue* fields and
            // the sweep with the Arc* fields; the digit reveal reads Clone*.
            HudTarget.Transcribing =>
            [
                HudTuningSection.Geometry, material, .. hueRotation,
                HudTuningSection.SweepMask, HudTuningSection.SweepRotation, HudTuningSection.Transcribing,
                HudTuningSection.DigitReveal, HudTuningSection.Simulation, HudTuningSection.Orientations,
            ],
            HudTarget.Rewriting =>
            [
                HudTuningSection.Geometry, material, .. hueRotation,
                HudTuningSection.SweepMask, HudTuningSection.SweepRotation, HudTuningSection.Rewriting,
                HudTuningSection.DigitReveal, HudTuningSection.Simulation, HudTuningSection.Orientations,
            ],
            // CreateNakedMaskPreview shows only the cone brush, which spins
            // with the Hue* fields; the sweep never shows.
            HudTarget.Cone =>
            [
                HudTuningSection.Geometry, material, .. hueRotation, HudTuningSection.Orientations,
            ],
            // CreateNakedMaskPreview shows only the sweep brush: no material.
            HudTarget.SweepMask =>
            [
                HudTuningSection.Geometry, HudTuningSection.SweepMask,
                HudTuningSection.SweepRotation, HudTuningSection.Orientations,
            ],
            HudTarget.MaskedCone =>
            [
                HudTuningSection.Geometry, material, .. hueRotation,
                HudTuningSection.SweepMask, HudTuningSection.SweepRotation, HudTuningSection.Orientations,
            ],
            // Its own reach rows, then the Recording rows it shares (fades and
            // material) and the material itself. Its cone is frozen like the
            // Recording outline's, so no hue rotation.
            HudTarget.Gauge =>
            [
                HudTuningSection.Gauge, HudTuningSection.Recording, material, HudTuningSection.Simulation,
            ],
            // CreateConicClonePreview paints the clone through
            // CloneSurfaceConfig: on the Colour material only the wheel's hue
            // layout reaches it (the page shows that subset), on the Grey
            // material the grey rows do.
            HudTarget.DigitReveal =>
            [
                HudTuningSection.DigitReveal, material, HudTuningSection.SweepMask,
            ],
            _ => throw new System.ArgumentOutOfRangeException(nameof(CurrentTarget), CurrentTarget, null),
        };
    }
}

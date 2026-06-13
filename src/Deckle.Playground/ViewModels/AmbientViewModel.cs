using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Lighting.Ambient;

namespace Deckle.Playground.ViewModels;

// ─── Ambient lighting Playground ViewModel ───────────────────────────────────
//
// Mirrors GeneralViewModel in Deckle.Settings : partial-properties pattern
// (MVVMTK0045 AOT-safe), _isSyncing guard around Load(), auto-save through
// PushToSettings on each property change.
//
// Backs the HDR tuning sandbox + pipeline mode toggle on AmbientPage.
// Persistence lands in <UserDataRoot>/modules/ambient/settings.json via
// AmbientSettingsService — same store the Settings AmbientPage and the
// canonical App-side AmbientEngine consume, so a slider drag here applies
// live on the next push tick.
//
// Touching a tuning slider implicitly switches Mode to Custom (the user is
// shaping their own thing, presets should not silently overwrite). This is
// the same contract as Settings → Ambient lighting. The mode flip happens
// inside each property's partial change method below ; PushToSettings is
// called once per change so the file write is debounced by JsonSettingsStore.
//
// Runtime state owned by AmbientPage (capture service, frame sampler,
// Hue REST output, preview cells, light-zone rects) is NOT here — its
// lifetime is tied to the Page instance, not to the persisted settings.
public partial class AmbientViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── HDR tuning ───────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial double ExposureEv { get; set; }

    [ObservableProperty]
    public partial double SaturationBoost { get; set; }

    [ObservableProperty]
    public partial int MinBrightness { get; set; }

    [ObservableProperty]
    public partial BrightnessCurveType BrightnessCurveType { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveParam { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveSCurveSteepness { get; set; }

    [ObservableProperty]
    public partial int ChangeThreshold { get; set; }

    [ObservableProperty]
    public partial double SmoothingAlpha { get; set; }

    // Zone-sampling band thickness. Sits alongside UseMultiLight
    // rather than the HDR tuning knobs : it's a structural setup
    // value (room/lamp layout, how much of the frame each lamp
    // summarises) not a per-scene content tuning, so it never flips
    // Mode to Custom. The mode picker swaps the active scale on the
    // engine ; both BorderDepth (share) and BorderCells (count) keep
    // their last user-set value across mode flips so a round-trip
    // back to a previous mode lands on the same setting.
    [ObservableProperty]
    public partial BorderThicknessMode BorderMode { get; set; }

    [ObservableProperty]
    public partial double BorderDepth { get; set; }

    [ObservableProperty]
    public partial int BorderCells { get; set; }

    // ── Mode & pipeline ──────────────────────────────────────────────────────

    [ObservableProperty]
    public partial AmbientMode Mode { get; set; }

    [ObservableProperty]
    public partial bool UseMultiLight { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // ── Setter side-effects ──────────────────────────────────────────────────
    //
    // Tuning sliders implicitly switch the mode to Custom — that's the
    // "stop overwriting my values" contract. Pipeline-shape changes
    // (UseMultiLight, Mode itself) don't transit through Custom because
    // they ARE the high-level intent the user is expressing.
    //
    // Mode = preset request : we go through ApplyPreset which copies the
    // preset's tunings onto the same AmbientSettings instance and saves.
    // Then Load() re-reads everything so the VM observes the new values
    // (the OnXxxChanged side-effects are suppressed by _isSyncing during
    // Load).

    partial void OnExposureEvChanged(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("Exposure", $"{value:F2} EV");
        AmbientSettingsService.Instance.Current.ExposureEv = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnSaturationBoostChanged(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("Saturation", $"{value * 100.0:F0} %");
        AmbientSettingsService.Instance.Current.SaturationBoost = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnMinBrightnessChanged(int value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("MinBrightness", value.ToString());
        AmbientSettingsService.Instance.Current.MinBrightness = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveTypeChanged(BrightnessCurveType value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BrightnessCurveType", value.ToString());
        AmbientSettingsService.Instance.Current.BrightnessCurveType = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveParamChanged(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BrightnessCurveParam", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveParam = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveSCurveSteepnessChanged(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BrightnessCurveSCurveSteepness", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveSCurveSteepness = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnChangeThresholdChanged(int value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("ChangeThreshold", value.ToString());
        AmbientSettingsService.Instance.Current.ChangeThreshold = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnSmoothingAlphaChanged(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("SmoothingAlpha", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.SmoothingAlpha = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBorderModeChanged(BorderThicknessMode value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BorderMode", value.ToString());
        AmbientSettingsService.Instance.Current.BorderMode = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBorderDepthChanged(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BorderDepth", $"{value * 100.0:F0} %");
        AmbientSettingsService.Instance.Current.BorderDepth = value;
        // No FlipToCustomMode — see the property doc above.
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBorderCellsChanged(int value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BorderCells", value.ToString());
        AmbientSettingsService.Instance.Current.BorderCells = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnUseMultiLightChanged(bool value)
    {
        if (_isSyncing) return;
        string modeLabel = value ? "per-zone" : "group";
        DecklePlaygroundSource.Log.PipelineModeChanged();
        DecklePlaygroundSource.Log.PipelineModeChangedDetail(modeLabel);
        AmbientSettingsService.Instance.Current.UseMultiLight = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        AmbientSettingsService.Instance.Current.Enabled = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnModeChanged(AmbientMode value)
    {
        if (_isSyncing) return;
        // ApplyPreset copies the preset's tuning snapshot onto every other
        // knob (saturation, exposure, gamma, etc.) and saves. Custom is a
        // no-op there — the preset code-path is a "snap back to a named
        // tuning", not "snap back to whatever the user had".
        AmbientSettingsService.Instance.ApplyPreset(value);
        // The store's Changed event will re-fire Load() on the page —
        // we don't need to re-read here.
    }

    // Helper that switches Mode to Custom without invoking ApplyPreset
    // (which would clobber the slider value the user just moved). Guarded
    // against re-entrancy so OnModeChanged doesn't fire ApplyPreset on
    // this synthetic transition.
    private void FlipToCustomMode()
    {
        var current = AmbientSettingsService.Instance.Current;
        if (current.Mode == AmbientMode.Custom && Mode == AmbientMode.Custom) return;

        current.Mode = AmbientMode.Custom;
        // Mirror into the VM under _isSyncing so OnModeChanged doesn't
        // re-trigger ApplyPreset. We deliberately don't call Save() here ;
        // the caller does its own Save() right after.
        bool prev = _isSyncing;
        _isSyncing = true;
        try   { Mode = AmbientMode.Custom; }
        finally { _isSyncing = prev; }
    }

    // ── Sync with AmbientSettingsService ─────────────────────────────────────

    public AmbientViewModel()
    {
        _isSyncing = true;

        ExposureEv                      = 0.0;
        SaturationBoost                 = 1.0;
        MinBrightness                   = 180;
        BrightnessCurveType             = BrightnessCurveType.Gamma;
        BrightnessCurveParam            = 1.8;
        BrightnessCurveSCurveSteepness  = 2.0;
        ChangeThreshold                 = 6;
        SmoothingAlpha                  = 0.30;
        BorderMode                      = BorderThicknessMode.Share;
        BorderDepth                     = 0.33;
        BorderCells                     = 8;
        Mode                            = AmbientMode.Game;
        UseMultiLight                   = false;
        Enabled                         = false;

        // _isSyncing stays true — Load() flips it to false.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var s = AmbientSettingsService.Instance.Current;
            ExposureEv                      = s.ExposureEv;
            SaturationBoost                 = s.SaturationBoost;
            MinBrightness                   = s.MinBrightness;
            BrightnessCurveType             = s.BrightnessCurveType;
            BrightnessCurveParam            = s.BrightnessCurveParam;
            BrightnessCurveSCurveSteepness  = s.BrightnessCurveSCurveSteepness;
            ChangeThreshold                 = s.ChangeThreshold;
            SmoothingAlpha                  = s.SmoothingAlpha;
            BorderMode                      = s.BorderMode;
            BorderDepth                     = s.BorderDepth;
            BorderCells                     = s.BorderCells;
            Mode                            = s.Mode;
            UseMultiLight                   = s.UseMultiLight;
            Enabled                         = s.Enabled;
        }
        finally
        {
            _isSyncing = false;
        }
    }
}

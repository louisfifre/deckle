using CommunityToolkit.Mvvm.ComponentModel;

namespace Deckle.Lighting.Ambient.Views;

// ─── Ambient lighting ViewModel ──────────────────────────────────────────────
//
// Mirrors GeneralViewModel in Deckle.Settings : partial-properties pattern
// (MVVMTK0045 AOT-safe), _isSyncing guard around Load(), auto-save through
// AmbientSettingsService.Save() on each property change.
//
// Owned by this module so the Settings AmbientPage composes its cards from
// the manifests in AmbientViewModel.Settings.cs. Persistence lands in
// <UserDataRoot>/modules/ambient/settings.json via AmbientSettingsService
// — the same store the App-side AmbientEngine reads, so a slider drag applies
// live on the next push tick.
//
// Only persisted settings live here : runtime state belongs to the engine
// and to the controls that draw it.
public partial class AmbientViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Tuning ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial double ExposureEv { get; set; }

    [ObservableProperty]
    public partial double SaturationBoost { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveX1 { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveY1 { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveX2 { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveY2 { get; set; }

    [ObservableProperty]
    public partial double SmoothingAlpha { get; set; }

    // ── Brightness floor ───────────────────────────────────────────────────

    [ObservableProperty]
    public partial int MinBrightness { get; set; }

    [ObservableProperty]
    public partial bool MinBrightnessEnabled { get; set; }

    // Zone-sampling band thickness : a structural setup value (room and lamp
    // layout), not a per-scene tuning. BorderDepth and BorderCells both keep their last value
    // across mode flips, so a round-trip lands on the same setting.
    [ObservableProperty]
    public partial BorderThicknessMode BorderMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BorderDepthPercent))]
    public partial double BorderDepth { get; set; }

    // BorderDepth as the percent the Settings slider shows (5..50). The store
    // keeps the 0.05..0.5 fraction the sampler reads; this is only its face.
    public double BorderDepthPercent
    {
        get => BorderDepth * 100.0;
        set => BorderDepth = value / 100.0;
    }

    [ObservableProperty]
    public partial int BorderCells { get; set; }

    // ── Pipeline ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool UseMultiLight { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // ── Command-enable flag ──────────────────────────────────────────────────
    //
    // Enabled only when the tuning surface (the seven tuning values, floor,
    // zone sampling, pipeline shape) differs from
    // the shipping defaults — drives the Reset all button in the page's title
    // row. Recomputed on every Load(), and every live-save edit round-trips
    // through Load() via the AmbientSettingsService.Changed observer, so the
    // button greys out the moment the surface is back at defaults.
    [ObservableProperty]
    public partial bool CanReset { get; set; }

    // ── Setter side-effects ──────────────────────────────────────────────────
    //
    // The tuning setters write the store and save. Pipeline-shape changes
    // (UseMultiLight, the floor) write and save too. The store's Changed event
    // re-fires Load() on the page so the VM observes the new values (the
    // OnXxxChanged side-effects are suppressed by _isSyncing during Load).

    partial void OnExposureEvChanged(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("Exposure", $"{value:F2} EV");
        AmbientSettingsService.Instance.Current.ExposureEv = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnSaturationBoostChanged(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("Saturation", $"{value * 100.0:F0} %");
        AmbientSettingsService.Instance.Current.SaturationBoost = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnMinBrightnessChanged(int value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("MinBrightness", value.ToString());
        AmbientSettingsService.Instance.Current.MinBrightness = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnMinBrightnessEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("MinBrightnessEnabled", value ? "on" : "off");
        AmbientSettingsService.Instance.Current.MinBrightnessEnabled = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveX1Changed(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BrightnessCurveX1", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveX1 = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveY1Changed(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BrightnessCurveY1", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveY1 = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveX2Changed(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BrightnessCurveX2", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveX2 = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveY2Changed(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BrightnessCurveY2", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveY2 = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnSmoothingAlphaChanged(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("SmoothingAlpha", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.SmoothingAlpha = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBorderModeChanged(BorderThicknessMode value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BorderMode", value.ToString());
        AmbientSettingsService.Instance.Current.BorderMode = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBorderDepthChanged(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BorderDepth", $"{value * 100.0:F0} %");
        AmbientSettingsService.Instance.Current.BorderDepth = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBorderCellsChanged(int value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BorderCells", value.ToString());
        AmbientSettingsService.Instance.Current.BorderCells = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnUseMultiLightChanged(bool value)
    {
        if (_isSyncing) return;
        string modeLabel = value ? "per-zone" : "group";
        DeckleAmbientSource.Log.PipelineModeChanged();
        DeckleAmbientSource.Log.PipelineModeChangedDetail(modeLabel);
        AmbientSettingsService.Instance.Current.UseMultiLight = value;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        AmbientSettingsService.Instance.Current.Enabled = value;
        AmbientSettingsService.Instance.Save();
    }

    // ── Sync with AmbientSettingsService ─────────────────────────────────────

    public AmbientViewModel()
    {
        _isSyncing = true;

        // Seeds from the POCO initializers, the single source of the defaults;
        // overwritten by Load().
        var d = new AmbientSettings();
        ExposureEv             = d.ExposureEv;
        SaturationBoost        = d.SaturationBoost;
        MinBrightness          = d.MinBrightness;
        MinBrightnessEnabled   = d.MinBrightnessEnabled;
        BrightnessCurveX1      = d.BrightnessCurveX1;
        BrightnessCurveY1      = d.BrightnessCurveY1;
        BrightnessCurveX2      = d.BrightnessCurveX2;
        BrightnessCurveY2      = d.BrightnessCurveY2;
        SmoothingAlpha         = d.SmoothingAlpha;
        BorderMode             = d.BorderMode;
        BorderDepth            = d.BorderDepth;
        BorderCells            = d.BorderCells;
        UseMultiLight          = d.UseMultiLight;
        Enabled                = d.Enabled;

        // _isSyncing stays true — Load() flips it to false.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var service = AmbientSettingsService.Instance;
            var s = service.Current;
            ExposureEv            = s.ExposureEv;
            SaturationBoost       = s.SaturationBoost;
            MinBrightness         = s.MinBrightness;
            MinBrightnessEnabled  = s.MinBrightnessEnabled;
            BrightnessCurveX1     = s.BrightnessCurveX1;
            BrightnessCurveY1     = s.BrightnessCurveY1;
            BrightnessCurveX2     = s.BrightnessCurveX2;
            BrightnessCurveY2     = s.BrightnessCurveY2;
            SmoothingAlpha        = s.SmoothingAlpha;
            BorderMode            = s.BorderMode;
            BorderDepth           = s.BorderDepth;
            BorderCells           = s.BorderCells;
            UseMultiLight         = s.UseMultiLight;
            Enabled               = s.Enabled;
        }
        finally
        {
            _isSyncing = false;
        }
        RecomputeCanReset();
    }

    // ── Reset to defaults ────────────────────────────────────────────────────
    //
    // Snaps the tuning surface back to the shipping defaults: the seven
    // tuning values, the floor, the zone-sampling band and the pipeline shape.
    // Connection state (bridge pairing, last group, monitor), the per-light
    // rows (LightZones, LightBrightness) and the pipeline on/off flag
    // are deliberately preserved : a reset shouldn't unpair the lights, forget a
    // per-light setup the user built light by light, or kill a running
    // pipeline. It writes straight onto the store and saves once : the VM
    // setters would save per field.
    public void ResetDefaults()
    {
        var service = AmbientSettingsService.Instance;
        var d   = new AmbientSettings();
        var cur = service.Current;

        cur.ExposureEv           = d.ExposureEv;
        cur.SaturationBoost      = d.SaturationBoost;
        cur.BrightnessCurveX1    = d.BrightnessCurveX1;
        cur.BrightnessCurveY1    = d.BrightnessCurveY1;
        cur.BrightnessCurveX2    = d.BrightnessCurveX2;
        cur.BrightnessCurveY2    = d.BrightnessCurveY2;
        cur.SmoothingAlpha       = d.SmoothingAlpha;
        cur.MinBrightnessEnabled = d.MinBrightnessEnabled;
        cur.MinBrightness        = d.MinBrightness;
        ApplyZoneSamplingDefaults(cur, d);
        cur.UseMultiLight      = d.UseMultiLight;

        service.Save();
    }

    private static void ApplyZoneSamplingDefaults(AmbientSettings cur, AmbientSettings d)
    {
        cur.BorderMode  = d.BorderMode;
        cur.BorderDepth = d.BorderDepth;
        cur.BorderCells = d.BorderCells;
    }

    // Compares the persisted tuning surface against a fresh AmbientSettings
    // (the compiled defaults). Exact equality is fine : the sliders snap to
    // discrete steps and the defaults are literals, so any user edit lands a
    // value the default never holds.
    private void RecomputeCanReset()
    {
        var service = AmbientSettingsService.Instance;
        var d = new AmbientSettings();
        var s = service.Current;
        CanReset =
            s.ExposureEv            != d.ExposureEv ||
            s.SaturationBoost       != d.SaturationBoost ||
            s.BrightnessCurveX1     != d.BrightnessCurveX1 ||
            s.BrightnessCurveY1     != d.BrightnessCurveY1 ||
            s.BrightnessCurveX2     != d.BrightnessCurveX2 ||
            s.BrightnessCurveY2     != d.BrightnessCurveY2 ||
            s.SmoothingAlpha        != d.SmoothingAlpha ||
            s.MinBrightnessEnabled  != d.MinBrightnessEnabled ||
            s.MinBrightness         != d.MinBrightness ||
            s.BorderMode            != d.BorderMode ||
            s.BorderDepth           != d.BorderDepth ||
            s.BorderCells           != d.BorderCells ||
            s.UseMultiLight         != d.UseMultiLight;
    }
}

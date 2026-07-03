using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Lighting.Ambient;

namespace Deckle.Playground;

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
    public partial bool MinBrightnessEnabled { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveX1 { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveY1 { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveX2 { get; set; }

    [ObservableProperty]
    public partial double BrightnessCurveY2 { get; set; }

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

    // ── Command-enable flag ──────────────────────────────────────────────────
    //
    // Enabled only when the tuning surface (HDR + zone sampling + mode) differs
    // from the shipping defaults — drives the Reset-to-defaults button in the
    // page's action bar. Recomputed on every Load(), and every live-save edit
    // round-trips through Load() via the AmbientSettingsService.Changed observer,
    // so the button greys out the moment the surface is back at defaults.
    [ObservableProperty]
    public partial bool CanReset { get; set; }

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

    partial void OnMinBrightnessEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("MinBrightnessEnabled", value ? "on" : "off");
        AmbientSettingsService.Instance.Current.MinBrightnessEnabled = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveX1Changed(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BrightnessCurveX1", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveX1 = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveY1Changed(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BrightnessCurveY1", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveY1 = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveX2Changed(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BrightnessCurveX2", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveX2 = value;
        FlipToCustomMode();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveY2Changed(double value)
    {
        if (_isSyncing) return;
        DecklePlaygroundSource.Log.TuningChanged("BrightnessCurveY2", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveY2 = value;
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
        // knob (saturation, exposure, Bézier curve, etc.) and saves. Custom is a
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
        MinBrightness          = 180;
        MinBrightnessEnabled   = true;
        BrightnessCurveX1      = 0.42;
        BrightnessCurveY1      = 0.00;
        BrightnessCurveX2      = 1.00;
        BrightnessCurveY2      = 1.00;
        ChangeThreshold        = 6;
        SmoothingAlpha         = 0.30;
        BorderMode             = BorderThicknessMode.Share;
        BorderDepth            = 0.33;
        BorderCells            = 8;
        Mode                   = AmbientMode.Game;
        UseMultiLight          = false;
        Enabled                = false;

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
            MinBrightness         = s.MinBrightness;
            MinBrightnessEnabled  = s.MinBrightnessEnabled;
            BrightnessCurveX1     = s.BrightnessCurveX1;
            BrightnessCurveY1     = s.BrightnessCurveY1;
            BrightnessCurveX2     = s.BrightnessCurveX2;
            BrightnessCurveY2     = s.BrightnessCurveY2;
            ChangeThreshold       = s.ChangeThreshold;
            SmoothingAlpha        = s.SmoothingAlpha;
            BorderMode            = s.BorderMode;
            BorderDepth           = s.BorderDepth;
            BorderCells           = s.BorderCells;
            Mode                  = s.Mode;
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
    // Snaps the tuning surface — HDR grading, zone-sampling band, and the Mode
    // preset — back to the AmbientSettings shipping defaults. Connection state
    // (bridge pairing, last group, monitor) and the pipeline on/off flag are
    // deliberately preserved : a reset shouldn't unpair the lights or kill a
    // running pipeline. We write straight onto the store and Save() once (rather
    // than going through the VM setters, which would fire a Save per field and
    // flip Mode to Custom on each) ; Save() raises Changed, the page's observer
    // reloads the VM and repaints every slider.
    public void ResetDefaults()
    {
        var d   = new AmbientSettings();
        var cur = AmbientSettingsService.Instance.Current;

        ApplyHdrDefaults(cur, d);
        ApplyZoneSamplingDefaults(cur, d);

        AmbientSettingsService.Instance.Save();
    }

    // Per-section resets — the HDR tuning card and the zone-sampling card each
    // carry their own reset. Both write straight onto the store and Save() once
    // (like ResetDefaults), so the Changed observer reloads the VM and repaints
    // only what changed. Splitting ResetDefaults this way keeps the page-level
    // "Reset all" and the two section resets reading from one defaults source.

    public void ResetHdrSection()
    {
        ApplyHdrDefaults(AmbientSettingsService.Instance.Current, new AmbientSettings());
        AmbientSettingsService.Instance.Save();
    }

    public void ResetZoneSamplingSection()
    {
        ApplyZoneSamplingDefaults(AmbientSettingsService.Instance.Current, new AmbientSettings());
        AmbientSettingsService.Instance.Save();
    }

    // The HDR grading surface + the Mode preset (Mode lives on the HDR card and
    // selects a whole tuning, so it resets with the grading it drives).
    private static void ApplyHdrDefaults(AmbientSettings cur, AmbientSettings d)
    {
        cur.ExposureEv            = d.ExposureEv;
        cur.SaturationBoost       = d.SaturationBoost;
        cur.MinBrightnessEnabled  = d.MinBrightnessEnabled;
        cur.MinBrightness         = d.MinBrightness;
        cur.BrightnessCurveX1     = d.BrightnessCurveX1;
        cur.BrightnessCurveY1     = d.BrightnessCurveY1;
        cur.BrightnessCurveX2     = d.BrightnessCurveX2;
        cur.BrightnessCurveY2     = d.BrightnessCurveY2;
        cur.ChangeThreshold       = d.ChangeThreshold;
        cur.SmoothingAlpha        = d.SmoothingAlpha;
        cur.Mode                  = d.Mode;
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
        var d = new AmbientSettings();
        var s = AmbientSettingsService.Instance.Current;
        CanReset =
            s.ExposureEv            != d.ExposureEv ||
            s.SaturationBoost       != d.SaturationBoost ||
            s.MinBrightnessEnabled  != d.MinBrightnessEnabled ||
            s.MinBrightness         != d.MinBrightness ||
            s.BrightnessCurveX1     != d.BrightnessCurveX1 ||
            s.BrightnessCurveY1     != d.BrightnessCurveY1 ||
            s.BrightnessCurveX2     != d.BrightnessCurveX2 ||
            s.BrightnessCurveY2     != d.BrightnessCurveY2 ||
            s.ChangeThreshold       != d.ChangeThreshold ||
            s.SmoothingAlpha        != d.SmoothingAlpha ||
            s.BorderMode            != d.BorderMode ||
            s.BorderDepth           != d.BorderDepth ||
            s.BorderCells           != d.BorderCells ||
            s.Mode                  != d.Mode;
    }
}

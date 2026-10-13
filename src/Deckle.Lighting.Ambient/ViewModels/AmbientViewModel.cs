using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Presets;

namespace Deckle.Lighting.Ambient;

// ─── Ambient lighting ViewModel ──────────────────────────────────────────────
//
// Mirrors GeneralViewModel in Deckle.Settings : partial-properties pattern
// (MVVMTK0045 AOT-safe), _isSyncing guard around Load(), auto-save through
// AmbientSettingsService.Save() on each property change.
//
// Owned by this module so the Settings AmbientPage composes its cards from
// the manifests in AmbientViewModel.Settings.cs and the Playground Ambient tab
// binds the same surface. Persistence lands in
// <UserDataRoot>/modules/ambient/settings.json via AmbientSettingsService
// — the same store the App-side AmbientEngine reads, so a slider drag applies
// live on the next push tick.
//
// Presets (Deckle.Presets, the family on AmbientSettingsService.Presets): a
// hand edit of one of the eight tuning values reports itself to the family,
// which keeps the values as Custom and turns the selection to Custom;
// picking a preset writes its values; picking Custom brings the hand-made
// values back. The floor, the zones, the capture rate and the pipeline
// shape are outside the presets and never touch the selection.
//
// Only persisted settings live here : runtime state belongs to the engine
// and to the controls that draw it.
public partial class AmbientViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Tuning (the eight values a preset fixes) ────────────────────────────

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
    public partial int ChangeThreshold { get; set; }

    [ObservableProperty]
    public partial double SmoothingAlpha { get; set; }

    // ── Brightness floor (outside the presets) ──────────────────────────────

    [ObservableProperty]
    public partial int MinBrightness { get; set; }

    [ObservableProperty]
    public partial bool MinBrightnessEnabled { get; set; }

    // Zone-sampling band thickness : a structural setup value (room and lamp
    // layout), not a per-scene tuning, so it never touches the preset
    // selection. BorderDepth and BorderCells both keep their last value
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

    // ── Preset & pipeline ────────────────────────────────────────────────────

    // The stored selection: a preset id from AmbientPresets, or
    // PresetFamily.Custom. Mirrors AmbientSettingsService.Presets.Selection.
    [ObservableProperty]
    public partial string Preset { get; set; }

    [ObservableProperty]
    public partial bool UseMultiLight { get; set; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    // Screen-capture rate in Hz, one of AmbientCaptureRate.Allowed. A
    // structural value like UseMultiLight: it never touches the preset
    // selection, and the app restarts a running engine when it changes.
    [ObservableProperty]
    public partial int CaptureFrameRateHz { get; set; }

    // ── Command-enable flag ──────────────────────────────────────────────────
    //
    // Enabled only when the tuning surface (preset selection, the eight tuning
    // values, floor, zone sampling, pipeline shape, capture rate) differs from
    // the shipping defaults — drives the Reset all button in the page's title
    // row. Recomputed on every Load(), and every live-save edit round-trips
    // through Load() via the AmbientSettingsService.Changed observer, so the
    // button greys out the moment the surface is back at defaults.
    [ObservableProperty]
    public partial bool CanReset { get; set; }

    // ── Setter side-effects ──────────────────────────────────────────────────
    //
    // The eight tuning setters write the store, report the edit to the preset
    // family (MarkEdited) and save. Pipeline-shape changes (UseMultiLight, the
    // floor, the capture rate) write and save only.
    //
    // Preset = selection request : the family writes the preset's values (or
    // the kept Custom values) onto the same AmbientSettings instance, then we
    // save. The store's Changed event re-fires Load() on the page so the VM
    // observes the new values (the OnXxxChanged side-effects are suppressed
    // by _isSyncing during Load).

    partial void OnExposureEvChanged(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("Exposure", $"{value:F2} EV");
        AmbientSettingsService.Instance.Current.ExposureEv = value;
        MarkEdited();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnSaturationBoostChanged(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("Saturation", $"{value * 100.0:F0} %");
        AmbientSettingsService.Instance.Current.SaturationBoost = value;
        MarkEdited();
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
        MarkEdited();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveY1Changed(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BrightnessCurveY1", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveY1 = value;
        MarkEdited();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveX2Changed(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BrightnessCurveX2", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveX2 = value;
        MarkEdited();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnBrightnessCurveY2Changed(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("BrightnessCurveY2", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.BrightnessCurveY2 = value;
        MarkEdited();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnChangeThresholdChanged(int value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("ChangeThreshold", value.ToString());
        AmbientSettingsService.Instance.Current.ChangeThreshold = value;
        MarkEdited();
        AmbientSettingsService.Instance.Save();
    }

    partial void OnSmoothingAlphaChanged(double value)
    {
        if (_isSyncing) return;
        DeckleAmbientSource.Log.TuningChanged("SmoothingAlpha", value.ToString("F2"));
        AmbientSettingsService.Instance.Current.SmoothingAlpha = value;
        MarkEdited();
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

    partial void OnCaptureFrameRateHzChanged(int value)
    {
        if (_isSyncing) return;
        // Snap an off-list value onto the closed set before it is stored, and
        // mirror the snapped value back into the VM under _isSyncing so this
        // handler does not run again for it.
        int snapped = AmbientCaptureRate.Snap(value);
        if (snapped != value)
        {
            _isSyncing = true;
            try   { CaptureFrameRateHz = snapped; }
            finally { _isSyncing = false; }
        }
        DeckleAmbientSource.Log.TuningChanged("CaptureFrameRateHz", snapped.ToString());
        AmbientSettingsService.Instance.Current.CaptureFrameRateHz = snapped;
        AmbientSettingsService.Instance.Save();
    }

    partial void OnPresetChanged(string value)
    {
        if (_isSyncing) return;
        var service = AmbientSettingsService.Instance;
        // Select writes the preset's eight values (or the kept Custom values)
        // onto the store and the selection with them; an unknown id writes
        // nothing. Save raises Changed, which reloads every surface.
        if (!service.Presets.Select(value)) return;
        DeckleAmbientSource.Log.TuningChanged("Preset", value);
        service.Save();
    }

    // Reports a hand edit of a tuning value to the preset family (which keeps
    // the live values as Custom and turns the selection to Custom) and mirrors
    // the selection into the VM under _isSyncing so OnPresetChanged does not
    // apply it again. The caller saves right after.
    private void MarkEdited()
    {
        var presets = AmbientSettingsService.Instance.Presets;
        presets.NotifyEdited();
        if (Preset == presets.Selection) return;

        bool prev = _isSyncing;
        _isSyncing = true;
        try   { Preset = presets.Selection; }
        finally { _isSyncing = prev; }
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
        ChangeThreshold        = d.ChangeThreshold;
        SmoothingAlpha         = d.SmoothingAlpha;
        BorderMode             = d.BorderMode;
        BorderDepth            = d.BorderDepth;
        BorderCells            = d.BorderCells;
        Preset                 = AmbientPresets.Default;
        UseMultiLight          = d.UseMultiLight;
        Enabled                = d.Enabled;
        CaptureFrameRateHz     = d.CaptureFrameRateHz;

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
            ChangeThreshold       = s.ChangeThreshold;
            SmoothingAlpha        = s.SmoothingAlpha;
            BorderMode            = s.BorderMode;
            BorderDepth           = s.BorderDepth;
            BorderCells           = s.BorderCells;
            Preset                = service.Presets.Selection;
            UseMultiLight         = s.UseMultiLight;
            Enabled               = s.Enabled;
            CaptureFrameRateHz    = AmbientCaptureRate.Snap(s.CaptureFrameRateHz);
        }
        finally
        {
            _isSyncing = false;
        }
        RecomputeCanReset();
    }

    // ── Reset to defaults ────────────────────────────────────────────────────
    //
    // Snaps the tuning surface back to the shipping defaults: the default
    // preset (which writes the eight tuning values and the selection), the
    // floor, the zone-sampling band, the pipeline shape and the capture rate.
    // Connection state (bridge pairing, last group, monitor), the per-light
    // rows (LightZones, LightBrightness), the pipeline on/off flag and the kept
    // Custom values are deliberately preserved : a reset shouldn't unpair the
    // lights, forget a per-light setup the user built light by light, kill a
    // running pipeline, or lose the values the user can still pick as Custom.
    // It writes straight onto the store and saves once : the VM setters would
    // save per field and report each as a hand edit.
    public void ResetDefaults()
    {
        var service = AmbientSettingsService.Instance;
        var d   = new AmbientSettings();
        var cur = service.Current;

        service.Presets.Select(service.Presets.Default.Id);
        cur.MinBrightnessEnabled = d.MinBrightnessEnabled;
        cur.MinBrightness        = d.MinBrightness;
        ApplyZoneSamplingDefaults(cur, d);
        cur.UseMultiLight      = d.UseMultiLight;
        cur.CaptureFrameRateHz = d.CaptureFrameRateHz;

        service.Save();
    }

    // Per-section resets for the Playground Ambient tab's card headers. Each
    // writes its section's defaults onto the store and saves once; the Changed
    // observer reloads every surface. The tuning section is the default preset
    // plus the floor; the zone section is the band shape and depth.
    public void ResetTuningSection()
    {
        var service = AmbientSettingsService.Instance;
        var d   = new AmbientSettings();
        var cur = service.Current;

        service.Presets.Select(service.Presets.Default.Id);
        cur.MinBrightnessEnabled = d.MinBrightnessEnabled;
        cur.MinBrightness        = d.MinBrightness;
        DeckleAmbientSource.Log.TuningChanged("TuningSection", "default");
        service.Save();
    }

    public void ResetZoneSamplingSection()
    {
        var service = AmbientSettingsService.Instance;
        ApplyZoneSamplingDefaults(service.Current, new AmbientSettings());
        DeckleAmbientSource.Log.TuningChanged("ZoneSampling", "default");
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
    // value the default never holds. The selection counts on its own: a
    // stored preset whose values drifted still reads as "not default".
    private void RecomputeCanReset()
    {
        var service = AmbientSettingsService.Instance;
        var d = new AmbientSettings();
        var s = service.Current;
        CanReset =
            service.Presets.Selection != AmbientPresets.Default ||
            AmbientTuning.From(s)   != AmbientTuning.From(d) ||
            s.MinBrightnessEnabled  != d.MinBrightnessEnabled ||
            s.MinBrightness         != d.MinBrightness ||
            s.BorderMode            != d.BorderMode ||
            s.BorderDepth           != d.BorderDepth ||
            s.BorderCells           != d.BorderCells ||
            s.UseMultiLight         != d.UseMultiLight ||
            AmbientCaptureRate.Snap(s.CaptureFrameRateHz) != d.CaptureFrameRateHz;
    }
}

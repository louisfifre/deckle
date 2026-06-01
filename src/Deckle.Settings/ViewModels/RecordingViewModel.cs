using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Audio;
using Deckle.Audio.Preprocessing;
using System.Globalization;

namespace Deckle.Settings.ViewModels;

// ViewModel for RecordingPage — bridges CaptureSettings (audio device,
// level window) to the XAML via x:Bind. Migrated from GeneralViewModel
// in slice S3 ; in pass2 the Behaviour properties (auto-paste + overlay)
// were moved to GeneralViewModel because they describe the app's overall
// behaviour, not the capture pipeline. What remains here is microphone
// device selection and voice level window calibration.
//
// Pattern: Load() pulls from the POCOs, property changes push back via
// PushToSettings(). The _isSyncing flag prevents re-saving during Load().
// Level window changes also push directly into the AudioLevelMapper
// statics via SettingsHost.ApplyLevelWindow so the HUD reflects the new
// curve on the next sub-window without restart.
public partial class RecordingViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Microphone ──────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial int AudioInputDeviceId { get; set; }

    partial void OnAudioInputDeviceIdChanged(int value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("Audio input device", value.ToString(CultureInfo.InvariantCulture));
        PushToSettings();
    }

    // ── Transcription pre-processing (DSP black box) ──────────────────────────

    [ObservableProperty]
    public partial bool PreprocessingEnabled { get; set; }

    partial void OnPreprocessingEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("Preprocessing.Enabled", value.ToString());
        // Opting in (re)starts the deferred-activation calibration: the DSP
        // stays inactive until the mic has been measured over the first few
        // recordings, then the engine flips it to Active or Dormant. We only
        // reset the state on enable — disabling leaves it irrelevant.
        if (value)
            CaptureSettingsService.Instance.Current.Preprocessing.Activation = PreprocessingActivation.Calibrating;
        PushToSettings();
    }

    // ── Level window (calibration) ──────────────────────────────────────────

    [ObservableProperty]
    public partial double LevelWindowMinDbfs { get; set; }

    [ObservableProperty]
    public partial double LevelWindowMaxDbfs { get; set; }

    [ObservableProperty]
    public partial double LevelWindowExponent { get; set; }

    [ObservableProperty]
    public partial bool LevelWindowAutoCalibration { get; set; }

    // Slider drags fire ValueChanged on every step (50+ events per drag),
    // so we keep the per-edit log line at Verbose level. PushToSettings
    // is fine on every step (the file save is debounced one level deeper
    // inside SettingsService); SettingsHost.ApplyLevelWindow ultimately
    // writes a few static fields in Audio.AudioLevelMapper, also free.
    partial void OnLevelWindowMinDbfsChanged(double value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChangedDetail("LevelWindow.MinDbfs", $"{value.ToString("F1", CultureInfo.InvariantCulture)} dBFS");
        PushToSettings();
        SettingsHost.ApplyLevelWindow?.Invoke(CaptureSettingsService.Instance.Current.LevelWindow);
    }

    partial void OnLevelWindowMaxDbfsChanged(double value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChangedDetail("LevelWindow.MaxDbfs", $"{value.ToString("F1", CultureInfo.InvariantCulture)} dBFS");
        PushToSettings();
        SettingsHost.ApplyLevelWindow?.Invoke(CaptureSettingsService.Instance.Current.LevelWindow);
    }

    partial void OnLevelWindowExponentChanged(double value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChangedDetail("LevelWindow.DbfsCurveExponent", value.ToString("F2", CultureInfo.InvariantCulture));
        PushToSettings();
        SettingsHost.ApplyLevelWindow?.Invoke(CaptureSettingsService.Instance.Current.LevelWindow);
    }

    partial void OnLevelWindowAutoCalibrationChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("LevelWindow.AutoCalibration", value.ToString());
        PushToSettings();
    }

    // ── Sync with CaptureSettingsService ────────────────────────────────────

    public RecordingViewModel()
    {
        _isSyncing = true;

        AudioInputDeviceId = -1;
        LevelWindowMinDbfs = -55;
        LevelWindowMaxDbfs = -32;
        LevelWindowExponent = 1.0;
        LevelWindowAutoCalibration = false;
        PreprocessingEnabled = false;

        // _isSyncing stays true — Load() will set it to false.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var capture = CaptureSettingsService.Instance.Current;

            AudioInputDeviceId = capture.AudioInputDeviceId;
            LevelWindowMinDbfs = capture.LevelWindow.MinDbfs;
            LevelWindowMaxDbfs = capture.LevelWindow.MaxDbfs;
            LevelWindowExponent = capture.LevelWindow.DbfsCurveExponent;
            LevelWindowAutoCalibration = capture.LevelWindow.AutoCalibrationEnabled;
            PreprocessingEnabled = capture.Preprocessing.Enabled;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void PushToSettings()
    {
        var capture = CaptureSettingsService.Instance.Current;

        capture.AudioInputDeviceId = AudioInputDeviceId;
        capture.LevelWindow.MinDbfs = (float)LevelWindowMinDbfs;
        capture.LevelWindow.MaxDbfs = (float)LevelWindowMaxDbfs;
        capture.LevelWindow.DbfsCurveExponent = (float)LevelWindowExponent;
        capture.LevelWindow.AutoCalibrationEnabled = LevelWindowAutoCalibration;
        capture.Preprocessing.Enabled = PreprocessingEnabled;

        CaptureSettingsService.Instance.Save();
    }

    public void ResetRecordingDefaults()
    {
        _isSyncing = true;
        try
        {
            AudioInputDeviceId = -1;
            LevelWindowMinDbfs = -55;
            LevelWindowMaxDbfs = -32;
            LevelWindowExponent = 1.0;
            LevelWindowAutoCalibration = false;
            PreprocessingEnabled = false;
        }
        finally { _isSyncing = false; }
        PushToSettings();
        SettingsHost.ApplyLevelWindow?.Invoke(CaptureSettingsService.Instance.Current.LevelWindow);
        DeckleSettingsSource.Log.SectionReset("Recording");
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deckle.Audio;
using System.Globalization;
using System.Threading.Tasks;

namespace Deckle.Settings;

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
        // On = active. The DSP self-adjusts on every recording (a no-op when
        // the mic is already at target); the mic level check advises whether
        // turning it on is worth it. No deferral, no auto-decision.
        PushToSettings();
    }

    // ── Microphone level check (the indicator) ────────────────────────────────
    //
    // Measures the mic and tells the user whether the pre-processing is worth
    // turning on — no transcription, no auto-decision, the toggle stays their
    // call. Four mutually-exclusive advice flags drive four InfoBars (IsOpen is
    // a plain bool, so no value converter is needed); MicResultDetail carries
    // the raw → normalised dBFS line shown alongside the verdict.

    [ObservableProperty]
    public partial bool IsMeasuringMic { get; set; }

    [ObservableProperty]
    public partial bool AdviceRecommended { get; set; }

    [ObservableProperty]
    public partial bool AdviceMarginal { get; set; }

    [ObservableProperty]
    public partial bool AdviceNotNeeded { get; set; }

    [ObservableProperty]
    public partial bool AdviceError { get; set; }

    [ObservableProperty]
    public partial string MicResultDetail { get; set; } = "";

    [RelayCommand]
    private async Task MeasureMicAsync()
    {
        if (IsMeasuringMic) return;
        IsMeasuringMic = true;
        ClearMicResult();
        try
        {
            var capture = CaptureSettingsService.Instance.Current;
            MicLevelAssessment a = await new MicLevelTester()
                .MeasureAsync(capture.AudioInputDeviceId, capture.Preprocessing);

            if (!a.HasSignal)
            {
                AdviceError = true;
                return;
            }

            MicResultDetail = string.Format(
                CultureInfo.InvariantCulture,
                "{0:F0} dBFS → {1:F0} dBFS",
                a.RawRmsDbfs, a.ProcessedRmsDbfs);

            AdviceRecommended = a.Advice == PreprocessingAdvice.Recommended;
            AdviceMarginal    = a.Advice == PreprocessingAdvice.Marginal;
            AdviceNotNeeded   = a.Advice == PreprocessingAdvice.NotNeeded;
        }
        finally
        {
            IsMeasuringMic = false;
        }
    }

    private void ClearMicResult()
    {
        AdviceRecommended = false;
        AdviceMarginal    = false;
        AdviceNotNeeded   = false;
        AdviceError       = false;
        MicResultDetail   = "";
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
        DeckleSettingsSource.Log.SettingChanged("LevelWindow.MinDbfs", $"{value.ToString("F1", CultureInfo.InvariantCulture)} dBFS");
        PushToSettings();
        SettingsHost.ApplyLevelWindow?.Invoke(CaptureSettingsService.Instance.Current.LevelWindow);
    }

    partial void OnLevelWindowMaxDbfsChanged(double value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("LevelWindow.MaxDbfs", $"{value.ToString("F1", CultureInfo.InvariantCulture)} dBFS");
        PushToSettings();
        SettingsHost.ApplyLevelWindow?.Invoke(CaptureSettingsService.Instance.Current.LevelWindow);
    }

    partial void OnLevelWindowExponentChanged(double value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("LevelWindow.DbfsCurveExponent", value.ToString("F2", CultureInfo.InvariantCulture));
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

        // Seed from the POCO initializers, not hand-copied literals — the same
        // single source the composer's reset defaults read. The level-window and
        // pre-processing values thus live in ONE place (the audio settings POCOs);
        // duplicating them here was the third copy that drifted. The device id has
        // no composed default (runtime hardware enumeration), so its -1 sentinel
        // stays an explicit literal here.
        var levelWindow = new LevelWindowSettings();
        AudioInputDeviceId = -1;
        LevelWindowMinDbfs = levelWindow.MinDbfs;
        LevelWindowMaxDbfs = levelWindow.MaxDbfs;
        LevelWindowExponent = levelWindow.DbfsCurveExponent;
        LevelWindowAutoCalibration = levelWindow.AutoCalibrationEnabled;
        PreprocessingEnabled = new PreprocessingSettings().Enabled;

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

    // ResetRecordingDefaults is gone: the composed values (pre-processing toggle,
    // voice-level group) now reset through their composers' ResetAll(), each value
    // driven back to the POCO-sourced default — no hand-copied literal list to keep
    // in sync. The page handler (ResetRecording_Click) orchestrates both composers
    // and the non-composed device-id reset, and owns the section-reset logging.
}

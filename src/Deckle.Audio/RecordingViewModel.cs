using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Deckle.Catalog;
using Deckle.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace Deckle.Audio;

// ViewModel for RecordingPage — bridges CaptureSettings to the XAML via
// x:Bind.
//
// Pattern: Load() pulls from the POCOs, property changes push back via
// PushToSettings(). The _isSyncing flag prevents re-saving during Load().
public partial class RecordingViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Microphone ──────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial int AudioInputDeviceId { get; set; }

    partial void OnAudioInputDeviceIdChanged(int value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Audio input device", value.ToString(CultureInfo.InvariantCulture));
        PushToSettings();
    }

    // ── Voice level ─────────────────────────────────────────────────────────
    //
    // dBFS, the POCO's unit: the level above which Deckle hears voice, read by
    // the segmenter, the low-audio warning and the HUD outline. A cleared
    // number field gives NaN, which is not pushed.

    [ObservableProperty]
    public partial double VoiceLevelDbfs { get; set; }

    partial void OnVoiceLevelDbfsChanged(double value)
    {
        if (_isSyncing || double.IsNaN(value)) return;
        DeckleSettingsUxSource.Log.SettingChanged("VoiceLevelDbfs", value.ToString(CultureInfo.InvariantCulture));
        PushToSettings();
    }

    // ── Maximum recording length ────────────────────────────────────────────
    //
    // Seconds, the POCO's unit; 0 = no limit (the default). The capture loop
    // snapshots it at recording start, so a change applies to the next take.

    [ObservableProperty]
    public partial int MaxRecordingDurationSeconds { get; set; }

    partial void OnMaxRecordingDurationSecondsChanged(int value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("MaxRecordingDurationSeconds", value.ToString(CultureInfo.InvariantCulture));
        PushToSettings();
    }

    // ── Transcription pre-processing (DSP black box) ──────────────────────────

    [ObservableProperty]
    public partial bool PreprocessingEnabled { get; set; }

    partial void OnPreprocessingEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Preprocessing.Enabled", value.ToString());
        // On = active. The DSP self-adjusts on every recording (a no-op when
        // the mic is already at target); the mic level check only advises
        // whether turning it on is worth it.
        PushToSettings();
    }

    // ── Microphone level check (the indicator) ────────────────────────────────
    //
    // Four mutually-exclusive advice flags drive four InfoBars (IsOpen is a
    // plain bool, so no value converter is needed); MicResultDetail carries the
    // raw → normalised dBFS line, and the margin to the voice level, shown
    // alongside the verdict.

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

    // Raised on the UI thread right before the microphone check or the
    // listening test opens the microphone, so the page closes whatever else
    // holds it (the voice meter's Listen). Two waveIn opens of one device at
    // once are not known to be safe, so the page never tries it.
    public event EventHandler? MicrophoneNeeded;

    [RelayCommand]
    private async Task MeasureMicAsync()
    {
        if (IsMeasuringMic) return;
        MicrophoneNeeded?.Invoke(this, EventArgs.Empty);
        IsMeasuringMic = true;
        ClearMicResult();
        try
        {
            var capture = CaptureSettingsService.Instance.Current;
            MicLevelAssessment a = await new MicLevelTester()
                .MeasureAsync(capture.AudioInputDeviceId, capture.Preprocessing, capture.VoiceLevelDbfs);

            if (!a.HasSignal)
            {
                AdviceError = true;
                return;
            }

            // Raw → processed (averages over the capture), then where the
            // loudest tenth of the 50 ms frames sits against the voice level,
            // which applies per frame, "above" or "below" by sign. Rounded to whole dB as
            // integers, so a margin just under zero never reads -0.
            int margin = (int)Math.Round(a.VoiceMarginDb);
            MicResultDetail = Loc.Format(
                margin >= 0 ? "RecordingPageMicResultAbove_Format" : "RecordingPageMicResultBelow_Format",
                (int)Math.Round(a.RawRmsDbfs),
                (int)Math.Round(a.ProcessedRmsDbfs),
                Math.Abs(margin));

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

    // The microphone-telemetry opt-in lives in RecordingTelemetryViewModel, the
    // module's contribution to the Diagnostics page.

    // ── Sync with CaptureSettingsService ────────────────────────────────────

    public RecordingViewModel()
    {
        _isSyncing = true;

        // Seed from the POCO initializers, not hand-copied literals — the same
        // single source the composer's reset defaults read. The device id has
        // no composed default (runtime hardware enumeration), so its -1 sentinel
        // stays an explicit literal here.
        AudioInputDeviceId = -1;
        VoiceLevelDbfs = new CaptureSettings().VoiceLevelDbfs;
        MaxRecordingDurationSeconds = new CaptureSettings().MaxRecordingDurationSeconds;
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
            VoiceLevelDbfs = capture.VoiceLevelDbfs;
            MaxRecordingDurationSeconds = capture.MaxRecordingDurationSeconds;
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
        if (!double.IsNaN(VoiceLevelDbfs)) capture.VoiceLevelDbfs = VoiceLevelDbfs;
        capture.MaxRecordingDurationSeconds = MaxRecordingDurationSeconds;
        capture.Preprocessing.Enabled = PreprocessingEnabled;

        CaptureSettingsService.Instance.Save();
    }
}

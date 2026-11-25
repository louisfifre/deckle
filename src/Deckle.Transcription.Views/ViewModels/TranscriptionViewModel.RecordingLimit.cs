using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Audio;

namespace Deckle.Transcription.Views;

// The maximum recording length, hosted on Dictation > General beside the other
// settings of a take. It is not a transcription setting: the value lives in the
// capture settings (CaptureSettings.MaxRecordingDurationSeconds, the file of the
// audio module) and the capture loop reads it, so this view model reads and
// writes CaptureSettingsService directly and leaves TranscriptionSettings alone.
// The Microphone page's own view model no longer touches the value, so the two
// never overwrite one another.
public partial class TranscriptionViewModel
{
    // Seconds, the POCO's unit; 0 = no limit (the default). The capture loop
    // snapshots it at recording start, so a change applies to the next take.
    [ObservableProperty]
    public partial int MaxRecordingDurationSeconds { get; set; }

    partial void OnMaxRecordingDurationSecondsChanged(int value)
    {
        if (_isSyncing) return;
        PushRecordingLimit();
    }

    // The page's two faces of that one value: a switch and a length in minutes
    // (RecordingLimit holds the conversion). Both read and write
    // MaxRecordingDurationSeconds, so the POCO stays the single truth. The last
    // length lives only here, for the session: switching off writes 0, and
    // switching on again restores it.
    private int _lastLimitMinutes = RecordingLimit.DefaultMinutes;

    public bool LimitRecordingDuration
    {
        get => RecordingLimit.IsLimited(MaxRecordingDurationSeconds);
        set
        {
            if (value == LimitRecordingDuration) return;
            if (value) MaxRecordingDurationSeconds = RecordingLimit.SecondsFor(_lastLimitMinutes);
            else
            {
                _lastLimitMinutes = RecordingLimit.MinutesToShow(MaxRecordingDurationSeconds, _lastLimitMinutes);
                MaxRecordingDurationSeconds = 0;
            }
        }
    }

    // A cleared number field gives NaN, which is ignored.
    public double MaxRecordingDurationMinutes
    {
        get => RecordingLimit.MinutesToShow(MaxRecordingDurationSeconds, _lastLimitMinutes);
        set
        {
            if (double.IsNaN(value)) return;
            _lastLimitMinutes = RecordingLimit.Clamp((int)Math.Round(value));
            if (LimitRecordingDuration) MaxRecordingDurationSeconds = RecordingLimit.SecondsFor(_lastLimitMinutes);
            else OnPropertyChanged(nameof(MaxRecordingDurationMinutes)); // seconds did not move
        }
    }

    // The two accessors above are derived from the seconds: tell the page when
    // they move, whichever way the seconds changed (a switch, the field, Load,
    // a reset).
    partial void OnMaxRecordingDurationSecondsChanged(int oldValue, int newValue)
    {
        // Any non-zero length, from the file or a field, is the one to restore.
        if (newValue > 0) _lastLimitMinutes = RecordingLimit.MinutesToShow(newValue, _lastLimitMinutes);
        OnPropertyChanged(nameof(LimitRecordingDuration));
        OnPropertyChanged(nameof(MaxRecordingDurationMinutes));
    }

    private void PushRecordingLimit()
    {
        CaptureSettingsService.Instance.Current.MaxRecordingDurationSeconds = MaxRecordingDurationSeconds;
        CaptureSettingsService.Instance.Save();
    }
}

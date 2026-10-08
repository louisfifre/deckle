using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace Deckle.Audio;

// ── Listening test ──────────────────────────────────────────────────────────
//
// Record a short take once, then play it back on the default output device
// (speakers or headphones) in four variants, so the user hears what the
// pre-processing does to their voice. Nothing is saved, nothing is
// transcribed, and there is no live microphone loop: the take lives in memory
// on this ViewModel, which lives as long as the cached page.
//
// The four variants are one RelayCommand<double>, the strength:
//   0    Off  — the raw microphone, untouched.
//   1    On   — the pre-processing with the current parameters, whether the
//               toggle above is on or not.
//   0.25 / 0.75 — blends of the two (ListeningTake.Render).
// The pre-processing toggle above shows which of Off / On is the current
// setting, so no fifth "current" variant is needed.
public partial class RecordingViewModel
{
    private readonly ListeningTester _listeningTester = new();
    private ListeningTake? _listeningTake;

    [ObservableProperty]
    public partial bool IsRecordingListeningTake { get; set; }

    [ObservableProperty]
    public partial bool HasListeningTake { get; set; }

    [ObservableProperty]
    public partial bool ListeningTestFailed { get; set; }

    [RelayCommand]
    private async Task RecordListeningTakeAsync()
    {
        if (IsRecordingListeningTake) return;
        ListeningTestFailed = false;
        IsRecordingListeningTake = true;
        try
        {
            // RecordAsync stops any playback first, so the speakers are not
            // heard in the new take.
            ListeningTake? take = await _listeningTester.RecordAsync(
                CaptureSettingsService.Instance.Current.AudioInputDeviceId);
            // A failed retake keeps the previous take: the play buttons stay
            // usable and the error InfoBar says what went wrong.
            if (take is not null) _listeningTake = take;
            HasListeningTake = _listeningTake is not null;
            ListeningTestFailed = take is null;
        }
        finally
        {
            IsRecordingListeningTake = false;
        }
    }

    // AllowConcurrentExecutions: clicking another variant while one plays must
    // interrupt it (PlayAsync cancels the clip in flight), not be blocked until
    // the first clip ends.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PlayListeningVariantAsync(double strength)
    {
        if (_listeningTake is null) return;
        float[] samples = _listeningTake.Render(
            strength, CaptureSettingsService.Instance.Current.Preprocessing);
        bool played = await _listeningTester.PlayAsync(samples);
        ListeningTestFailed = !played;
    }

    // Called by the page when the user navigates away: the clip must not keep
    // playing behind another Settings page.
    public void StopListeningPlayback() => _listeningTester.StopPlayback();
}

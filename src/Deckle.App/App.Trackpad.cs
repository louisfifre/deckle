using Deckle.Input;
using Deckle.Input.Trackpad;

namespace Deckle.App;

// The raw input host runs while the gesture engine needs frames. Raw contact
// frames are no longer recorded to disk (2026-11-13): the host's periodic
// frame-count rollup in the log is what remains of them.
public partial class App
{
    private RawInputHost? _inputHost;
    private TrackpadEngine? _trackpadEngine;

    // Reconcile runs on every trackpad settings change, tuning included: a
    // host that keeps failing is reported once until it starts again.
    private bool _trackpadHostFailureReported;

    private void InitializeTrackpad()
    {
        _inputHost = new RawInputHost();
        _trackpadEngine = new TrackpadEngine(_inputHost);

        TrackpadSettingsService.Instance.Changed += ReconcileTrackpad;
        ReconcileTrackpad();
    }

    // Start and Stop are idempotent, so unrelated settings changes cost nothing.
    private void ReconcileTrackpad()
    {
        if (_inputHost is null || _trackpadEngine is null) return;

        bool needsFrames = TrackpadSettingsService.Instance.Current.Enabled;

        bool inputAvailable = !needsFrames || _inputHost.Start();
        if (!inputAvailable && !_trackpadHostFailureReported)
        {
            DeckleAppSource.Log.InputConsumerUnavailable("trackpad", "host_start_failed");
            _trackpadHostFailureReported = true;
        }
        else if (inputAvailable && needsFrames)
        {
            _trackpadHostFailureReported = false;
        }

        if (needsFrames && inputAvailable) _trackpadEngine.Start();
        else                               _trackpadEngine.Stop();

        if (!needsFrames) _inputHost.Stop();
    }

    // An injected primary button must never outlive the process, so the engine
    // and its safety release go down first, then the input thread.
    private void ShutdownTrackpad()
    {
        _trackpadEngine?.Dispose();
        _inputHost?.Dispose();
    }
}

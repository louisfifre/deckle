using Deckle.Input;
using Deckle.Input.Trackpad;

namespace Deckle.App;

// The frame recorder is deliberately independent of the master switch, so real
// Bluetooth sessions can be recorded with the recognizer off.
public partial class App
{
    private RawInputHost? _inputHost;
    private TrackpadEngine? _trackpadEngine;
    private ContactFrameRecorder? _frameRecorder;

    private void InitializeTrackpad()
    {
        _inputHost = new RawInputHost();
        _frameRecorder = new ContactFrameRecorder();
        _trackpadEngine = new TrackpadEngine(_inputHost);

        // The recorder listens permanently; it ignores frames while not
        // recording. Device lines keep a session self-describing when the
        // trackpad (re)connects mid-recording.
        _inputHost.FrameAssembled        += frame => _frameRecorder.OnFrame(frame);
        _inputHost.TouchpadDeviceConnected += device => _frameRecorder.NoteDevice(device);

        TrackpadSettingsService.Instance.Changed += ReconcileTrackpad;
        ReconcileTrackpad();
    }

    // Start and Stop are idempotent, so unrelated settings changes cost nothing.
    private void ReconcileTrackpad()
    {
        if (_inputHost is null || _trackpadEngine is null || _frameRecorder is null) return;

        var settings = TrackpadSettingsService.Instance.Current;
        bool needsFrames = settings.Enabled || settings.RecordFrames;

        bool inputAvailable = !needsFrames || _inputHost.Start();

        if (settings.Enabled && inputAvailable) _trackpadEngine.Start();
        else                                    _trackpadEngine.Stop();

        if (settings.RecordFrames && inputAvailable)
        {
            if (!_frameRecorder.IsRecording) _frameRecorder.Start(_inputHost.Touchpads);
        }
        else
        {
            _frameRecorder.Stop();
        }

        if (!needsFrames) _inputHost.Stop();
    }

    // An injected primary button must never outlive the process, so the engine
    // and its safety release go down first, then the recorder, then the input thread.
    private void ShutdownTrackpad()
    {
        _trackpadEngine?.Dispose();
        _frameRecorder?.Stop();
        _inputHost?.Dispose();
    }
}

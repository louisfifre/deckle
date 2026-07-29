using Deckle.Input;
using Deckle.Input.Trackpad;

namespace Deckle.App;

// Trackpad module composition. The App owns the three pieces and keeps
// them reconciled with the persisted settings, same posture as the
// Ambient engine observer:
//
//   RawInputHost      — dedicated Raw Input thread; runs whenever anyone
//                       consumes frames (drag engine or frame recorder),
//                       stops when both are off.
//   TrackpadEngine    — three-finger drag (master switch).
//   ContactFrameRecorder — raw frame telemetry to JSONL (diagnostics
//                       toggle, deliberately independent of the master
//                       switch so real Bluetooth sessions can be
//                       recorded with the recognizer off).
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

    // Idempotent settings → runtime reconciliation, called at boot and on
    // every settings flush. Start/Stop on the host and engine are
    // themselves idempotent, so re-running on unrelated settings changes
    // costs nothing.
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

    // Called from QuitApp — an injected primary button must never outlive
    // the process, so the engine (safety release) goes down first, then
    // the recorder flushes, then the input thread.
    private void ShutdownTrackpad()
    {
        _trackpadEngine?.Dispose();
        _frameRecorder?.Stop();
        _inputHost?.Dispose();
    }
}

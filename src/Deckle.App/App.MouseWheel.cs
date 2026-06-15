using Deckle.Input;

namespace Deckle.App;

// Mouse-wheel capture composition — Palier 0 of the wheel→touchpad work.
// The shared input host already reads the wheel (KeyboardInputHost raises
// WheelObserved); here the App attaches the JSONL recorder and reconciles it
// with the persisted MouseWheelSettings. Same posture as App.Trackpad's
// frame recorder: a diagnostic capture, independent of any master switch.
//
// The recorder takes a reference on the shared host for the duration of a
// capture, so wheel events flow even when autocorrect (the host's other
// consumer) is off — and releasing it never pulls the host from under
// autocorrect.
public partial class App
{
    private WheelEventRecorder? _wheelRecorder;
    private bool _wheelRecording;

    private void InitializeMouseWheel()
    {
        if (_keyboardMouseHost is null) return; // shared host not created — nothing to record

        _wheelRecorder = new WheelEventRecorder();
        _keyboardMouseHost.WheelObserved += OnWheelObserved;

        MouseWheelSettingsService.Instance.Changed += ReconcileMouseWheel;
        ReconcileMouseWheel();
    }

    private void OnWheelObserved(MouseWheelEvent e) => _wheelRecorder?.OnWheel(e);

    // Idempotent settings → runtime reconciliation, called at boot and on
    // every settings flush. The recorder ignores events while not recording,
    // but the host must be running for any to arrive — so a capture takes a
    // host reference on start and releases it on stop (balanced; the host
    // unwinds only when its last consumer leaves).
    private void ReconcileMouseWheel()
    {
        if (_keyboardMouseHost is null || _wheelRecorder is null) return;

        bool record = MouseWheelSettingsService.Instance.Current.RecordEvents;
        if (record && !_wheelRecording)
        {
            if (_keyboardMouseHost.Start())
            {
                _wheelRecorder.Start();
                _wheelRecording = true;
            }
        }
        else if (!record && _wheelRecording)
        {
            _wheelRecorder.Stop();
            _keyboardMouseHost.Stop();
            _wheelRecording = false;
        }
    }

    // Called from QuitApp. Releases the host reference a capture holds and
    // flushes the recorder; the shared host itself unwinds once autocorrect
    // releases its own reference too.
    private void ShutdownMouseWheel()
    {
        if (!_wheelRecording) return;
        _wheelRecorder?.Stop();
        _keyboardMouseHost?.Stop();
        _wheelRecording = false;
    }
}

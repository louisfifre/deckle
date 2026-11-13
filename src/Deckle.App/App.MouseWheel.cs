using Deckle.Input;

namespace Deckle.App;

// A diagnostic capture, independent of any module switch and held under
// Detailed logs (Settings > Diagnostics). A capture holds its
// own reference on the shared input host, so wheel events flow whatever the
// other consumers do, and releasing it never pulls the host from under them.
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
        Diagnostics.Logging.LoggingSettingsService.Instance.Changed += ReconcileMouseWheel;
        ReconcileMouseWheel();
    }

    private void OnWheelObserved(MouseWheelEvent e) => _wheelRecorder?.OnWheel(e);

    private void ReconcileMouseWheel()
    {
        if (_keyboardMouseHost is null || _wheelRecorder is null) return;

        // A developer capture under Settings > Diagnostics > Detailed logs:
        // the switch above it stops it whatever its own toggle says.
        bool record = Diagnostics.Logging.LoggingSettingsService.Instance.Current.DetailedLogs
                   && MouseWheelSettingsService.Instance.Current.RecordEvents;
        if (record && !_wheelRecording)
        {
            if (_keyboardMouseHost.Start())
            {
                _wheelRecorder.Start();
                _wheelRecording = true;
            }
            else
            {
                DeckleAppSource.Log.InputConsumerUnavailable("mouse-wheel-recorder", "host_start_failed");
            }
        }
        else if (!record && _wheelRecording)
        {
            _wheelRecorder.Stop();
            _keyboardMouseHost.Stop();
            _wheelRecording = false;
        }
    }

    private void ShutdownMouseWheel()
    {
        if (!_wheelRecording) return;
        _wheelRecorder?.Stop();
        _keyboardMouseHost?.Stop();
        _wheelRecording = false;
    }
}

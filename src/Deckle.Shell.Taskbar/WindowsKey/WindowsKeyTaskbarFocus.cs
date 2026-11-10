using Deckle.Input;

namespace Deckle.Shell.Taskbar;

// The Windows key shortcut at runtime: while the setting is on it listens to the
// shared keyboard host and, for a Windows key pressed on its own, brings the
// taskbar forward in place of Start (see WindowsKeyTapDetector for how a lone
// press is told). The App calls Reconcile with the setting's value, at start and
// at every change.
//
// The host is shared and reference-counted: it is started when the setting turns
// on and released when it turns off, so a person who never turns it on costs
// the keyboard nothing. Key events arrive on the host's input thread, where the
// two sends are made; each is a handful of microseconds.
public sealed class WindowsKeyTaskbarFocus : IDisposable
{
    private readonly IKeyboardInputHost _host;
    private readonly IWindowsKeyInjector _injector;
    private readonly WindowsKeyTapDetector _detector = new();

    // Reconcile and Dispose, which may block on the host. Never taken on the
    // input thread, so waiting on that thread under it cannot deadlock.
    private readonly object _stateGate = new();

    // The detector's own gate, held only for one event or one switch.
    private readonly object _detectorGate = new();

    private bool _armed;

    // A send Windows refused opens an incident; the next send it accepts closes it.
    private bool _sendFailing;

    public WindowsKeyTaskbarFocus(IKeyboardInputHost host, IWindowsKeyInjector injector)
    {
        _host = host;
        _injector = injector;
    }

    public bool IsArmed
    {
        get { lock (_stateGate) return _armed; }
    }

    /// <summary>Brings the runtime in line with the setting; a call that changes nothing costs nothing.</summary>
    public void Reconcile(bool wanted)
    {
        lock (_stateGate)
        {
            if (wanted == _armed) return;
            if (wanted) Arm();
            else Disarm();
        }
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_armed) Disarm();
        }
    }

    private void Arm()
    {
        _host.KeyReceived += OnKey;
        if (!_host.Start())
        {
            _host.KeyReceived -= OnKey;
            DeckleShellTaskbarSource.Log.WindowsKeyFocusUnavailable("host_start_failed");
            return;
        }

        lock (_detectorGate) _detector.Enabled = true;
        _armed = true;
        DeckleShellTaskbarSource.Log.WindowsKeyFocusArmed();
    }

    private void Disarm()
    {
        lock (_detectorGate) _detector.Enabled = false;
        _host.KeyReceived -= OnKey;
        _host.Stop();
        _armed = false;
        DeckleShellTaskbarSource.Log.WindowsKeyFocusDisarmed();
    }

    private void OnKey(KeyboardKeyEvent key)
    {
        WindowsKeyAction action;
        lock (_detectorGate) action = _detector.OnKey(key);

        switch (action)
        {
            case WindowsKeyAction.SuppressStart:
                NoteSend("suppress_start", _injector.SuppressStart());
                break;

            case WindowsKeyAction.FocusTaskbar:
                bool sent = _injector.FocusTaskbar();
                NoteSend("focus_taskbar", sent);
                DeckleShellTaskbarSource.Log.WindowsKeyTapHandledDetail(sent);
                break;
        }
    }

    private void NoteSend(string step, bool sent)
    {
        if (!sent)
        {
            if (_sendFailing) return;
            _sendFailing = true;
            DeckleShellTaskbarSource.Log.WindowsKeyInjectionFailed(step, _injector.LastWin32Error);
        }
        else if (_sendFailing)
        {
            _sendFailing = false;
            DeckleShellTaskbarSource.Log.WindowsKeyInjectionRecovered();
        }
    }
}

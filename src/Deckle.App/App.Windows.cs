using Deckle.App.Diagnostics;
using Deckle.Playground;

namespace Deckle.App;

public partial class App
{
    private void ShowLogWindowLazy()
    {
        if (_logWindow is null)
        {
            _logWindow = new LogWindow();
            AppDiagnosticsBootstrap.AttachLogWindowSink(_logWindow);
            _logWindow.SetRecordingState(_lastRecordingState);
            ApplyThemeToSingle(_logWindow);
        }
        _logWindow.ShowAndActivate();
    }

    private void ShowSettingsWindowLazy(string? pageTag = null)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new Settings.SettingsWindow
            {
                OnShowLogsRequested = () => ShowLogWindowLazy(),
            };
            ApplyThemeToSingle(_settingsWindow);
        }
        _settingsWindow.ShowAndActivate(pageTag);
    }

    private void ShowPlaygroundLazy()
    {
        if (_playgroundWindow is null)
        {
            _playgroundWindow = new PlaygroundWindow();
            // Playground owns heavy runtime resources, so close destroys it.
            _playgroundWindow.Closed += (_, _) => _playgroundWindow = null;
            _playgroundWindow.SetRecordingState(_lastRecordingState);
            ApplyThemeToSingle(_playgroundWindow);
        }
        _playgroundWindow.ShowAndActivate();
    }
}

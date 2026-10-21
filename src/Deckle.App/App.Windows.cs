using System.Diagnostics;
using Deckle.App;
using Deckle.Diagnostics;
using Deckle.Playground;

namespace Deckle.App;

public partial class App
{
    private void ShowLogWindowLazy()
    {
        if (_logWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            var window = CreateWindowOrLog("log", () => new LogWindow());
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Log);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Log);
            window.Closed += (_, _) =>
            {
                AppDiagnosticsBootstrap.DetachLogWindowSink(window);
                if (ReferenceEquals(_logWindow, window)) _logWindow = null;
            };

            _logWindow = window;
            AppDiagnosticsBootstrap.AttachLogWindowSink(window);
            window.SetRecordingState(_lastRecordingState);
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("log", sw.ElapsedMilliseconds);
        }
        _logWindow.ShowAndActivate();
    }

    private void ShowSettingsWindowLazy(string? pageTag = null)
    {
        if (_settingsWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            var window = CreateWindowOrLog("settings", () => new Settings.SettingsWindow
            {
                OnShowLogsRequested = () => ShowLogWindowLazy(),
            });
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Settings);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Settings);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
            };

            _settingsWindow = window;
            window.SetRecordingState(_lastRecordingState);
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("settings", sw.ElapsedMilliseconds);
        }
        _settingsWindow.ShowAndActivate(pageTag);
    }

    private void ShowPlaygroundLazy()
    {
        if (_playgroundWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            var window = CreateWindowOrLog("playground", () => new PlaygroundWindow());
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Playground);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Playground);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_playgroundWindow, window)) _playgroundWindow = null;
            };

            _playgroundWindow = window;
            window.SetRecordingState(_lastRecordingState);
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("playground", sw.ElapsedMilliseconds);
        }
        _playgroundWindow.ShowAndActivate();
    }

    // The constructor is where XAML parsing and page composition fail. The
    // exception is named here, then rethrown so the crash net and the caller
    // keep their current behaviour.
    private static T CreateWindowOrLog<T>(string name, Func<T> create)
        where T : Microsoft.UI.Xaml.Window
    {
        T window;
        try
        {
            window = create();
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.WindowOpenFailed(name, ex.GetType().Name, ex.Message);
            throw;
        }
        DeckleAppSource.Log.WindowCreated(name);
        return window;
    }
}

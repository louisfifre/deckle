using System.Diagnostics;
using System.Diagnostics.Tracing;
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
            bool measure = DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing);
            var sw = measure ? Stopwatch.StartNew() : null;
            var window = new LogWindow();
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
            if (sw is not null)
                DeckleWindowingSource.Log.WindowLoadComplete("log", sw.ElapsedMilliseconds);
        }
        _logWindow.ShowAndActivate();
    }

    private void ShowSettingsWindowLazy(string? pageTag = null)
    {
        if (_settingsWindow is null)
        {
            bool measure = DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing);
            var sw = measure ? Stopwatch.StartNew() : null;
            var window = new Settings.SettingsWindow
            {
                OnShowLogsRequested = () => ShowLogWindowLazy(),
            };
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Settings);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Settings);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
            };

            _settingsWindow = window;
            ApplyThemeToSingle(window);
            if (sw is not null)
                DeckleWindowingSource.Log.WindowLoadComplete("settings", sw.ElapsedMilliseconds);
        }
        _settingsWindow.ShowAndActivate(pageTag);
    }

    private void ShowPlaygroundLazy()
    {
        if (_playgroundWindow is null)
        {
            bool measure = DeckleWindowingSource.Log.IsEnabled(
                EventLevel.Verbose, (EventKeywords)Keywords.Windowing);
            var sw = measure ? Stopwatch.StartNew() : null;
            var window = new PlaygroundWindow();
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Playground);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Playground);
            // Playground owns heavy runtime resources, so close destroys it.
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_playgroundWindow, window)) _playgroundWindow = null;
            };

            _playgroundWindow = window;
            window.SetRecordingState(_lastRecordingState);
            ApplyThemeToSingle(window);
            if (sw is not null)
                DeckleWindowingSource.Log.WindowLoadComplete("playground", sw.ElapsedMilliseconds);
        }
        _playgroundWindow.ShowAndActivate();
    }
}

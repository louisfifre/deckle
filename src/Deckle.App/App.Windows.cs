using System.Diagnostics;
using Deckle.App;
using Deckle.Diagnostics;

namespace Deckle.App;

public partial class App
{
    // Each open window shows the recording state in its icons.
    private void ApplyRecordingStateToWindows(bool isRecording)
    {
        _logWindow?.SetRecordingState(isRecording);
        _publishedHost?.SetRecordingState(isRecording);
        _settingsWindow?.SetRecordingState(isRecording);
    }

    private void ShowLogWindowLazy()
    {
        EnsureLogWindow().ShowAndActivate();
        DeckleAppSource.Log.WindowOpened("log");
    }

    // The window created and wired, not shown: the self-check shows it without
    // activating it (SelfCheck/OffscreenWindow.cs).
    private LogWindow EnsureLogWindow()
    {
        if (_logWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            // Settings opens on the Diagnostics page: it edits the verbosity
            // controls that decide what this window may show.
            var window = CreateWindowOrLog("log", () => new LogWindow
            {
                OnShowSettingsRequested = () => ShowSettingsWindowLazy(
                    Diagnostics.Logging.Views.DiagnosticsSettingsModule.PageTag),
            });
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
        return _logWindow;
    }

    private void ShowSettingsWindowLazy(string? pageTag = null)
    {
        EnsureSettingsWindow().ShowAndActivate(pageTag);
        DeckleAppSource.Log.WindowOpened("settings");
    }

    private Settings.SettingsWindow EnsureSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            var window = CreateWindowOrLog("settings", () => new Settings.SettingsWindow
            {
                OnShowLogsRequested = ShowLogWindowLazy,
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
        return _settingsWindow;
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

using System.Diagnostics;
using Deckle.App;
using Deckle.Diagnostics;
using Deckle.Hosting;

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
            var window = WindowLifecycle.CreateOrLog("log", () => new LogWindow
            {
                OnShowSettingsRequested = () => ShowSettingsWindowLazy(
                    Diagnostics.Logging.Views.DiagnosticsSettingsModule.PageTag),
            });
            WindowPlacement.Restore(window, SecondaryWindowPlacement.Log);
            window.AppWindow.Closing += (_, _) =>
                WindowPlacement.Save(window, SecondaryWindowPlacement.Log);
            window.Closed += (_, _) =>
            {
                AppDiagnosticsBootstrap.DetachLogWindowSink(window);
                if (ReferenceEquals(_logWindow, window)) _logWindow = null;
            };

            _logWindow = window;
            AppDiagnosticsBootstrap.AttachLogWindowSink(window);
            window.SetRecordingState(_lastRecordingState);
            WindowTheme.Apply(window);
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
            var window = WindowLifecycle.CreateOrLog("settings", () => new Settings.SettingsWindow
            {
                OnShowLogsRequested = ShowLogWindowLazy,
            });
            WindowPlacement.Restore(window, SecondaryWindowPlacement.Settings);
            window.AppWindow.Closing += (_, _) =>
                WindowPlacement.Save(window, SecondaryWindowPlacement.Settings);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
            };

            _settingsWindow = window;
            window.SetRecordingState(_lastRecordingState);
            WindowTheme.Apply(window);
            DeckleWindowingSource.Log.WindowLoadComplete("settings", sw.ElapsedMilliseconds);
        }
        return _settingsWindow;
    }
}

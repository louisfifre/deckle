using System.Diagnostics;
using Deckle.App;
using Deckle.Diagnostics;
using Deckle.Playground;

namespace Deckle.App;

public partial class App
{
    private void ShowLogWindowLazy() => EnsureLogWindow().ShowAndActivate();

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
                OnShowPlaygroundRequested = ShowPlaygroundLazy,
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
            window.SetPlaygroundAvailable(DeveloperModeEnabled);
            AppDiagnosticsBootstrap.AttachLogWindowSink(window);
            window.SetRecordingState(_lastRecordingState);
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("log", sw.ElapsedMilliseconds);
        }
        return _logWindow;
    }

    private void ShowSettingsWindowLazy(string? pageTag = null)
        => EnsureSettingsWindow().ShowAndActivate(pageTag);

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
                OnShowPlaygroundRequested = ShowPlaygroundLazy,
            });
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Settings);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Settings);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
            };

            _settingsWindow = window;
            window.SetPlaygroundAvailable(DeveloperModeEnabled);
            window.SetRecordingState(_lastRecordingState);
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("settings", sw.ElapsedMilliseconds);
        }
        return _settingsWindow;
    }

    private void ShowPlaygroundLazy()
    {
        // A developer surface: no path opens it while the setting is off.
        if (!DeveloperModeEnabled) return;
        EnsurePlaygroundWindow().ShowAndActivate();
    }

    private PlaygroundWindow EnsurePlaygroundWindow()
    {
        if (_playgroundWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            var window = CreateWindowOrLog("playground", () => new PlaygroundWindow
            {
                OnShowSettingsRequested = () => ShowSettingsWindowLazy(),
                OnShowLogsRequested = ShowLogWindowLazy,
            });
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
        return _playgroundWindow;
    }

    // The Deckle Mail window, on its built-in sample until the engine is
    // wired. A preview for the maintainer: it opens only under the developer
    // setting that governs the Playground, and no module or setting of its
    // own registers it yet (that comes with the real engine).
    private void ShowMailLazy()
    {
        if (!DeveloperModeEnabled) return;
        EnsureMailWindow().ShowAndActivate();
    }

    private Mail.Hub.MailWindow EnsureMailWindow()
    {
        if (_mailWindow is null)
        {
            // Not gated: one Stopwatch per window per process, and the provider
            // declares this milestone ungoverned.
            var sw = Stopwatch.StartNew();
            var window = CreateWindowOrLog("mail", () => new Mail.Hub.MailWindow());
            SecondaryWindowPlacement.Restore(window, SecondaryWindowPlacement.Mail);
            window.AppWindow.Closing += (_, _) =>
                SecondaryWindowPlacement.Save(window, SecondaryWindowPlacement.Mail);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_mailWindow, window)) _mailWindow = null;
            };

            _mailWindow = window;
            window.SetRecordingState(_lastRecordingState);
            ApplyThemeToSingle(window);
            DeckleWindowingSource.Log.WindowLoadComplete("mail", sw.ElapsedMilliseconds);
        }
        return _mailWindow;
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

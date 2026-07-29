using Deckle.App;

namespace Deckle.App;

public partial class App
{
    private const string ResidentMutexName = @"Local\Deckle.Resident";
    private Mutex? _residentMutex;

    private bool TryAcquireResidentOwnership()
    {
        var mutex = new Mutex(initiallyOwned: false, ResidentMutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The previous process died without orderly teardown. Windows grants
            // this thread ownership; recovery is safe because no old hook remains.
            acquired = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return false;
        }

        _residentMutex = mutex;
        return true;
    }

    private void ReleaseResidentOwnership()
    {
        Mutex? mutex = _residentMutex;
        _residentMutex = null;
        if (mutex is null) return;

        try { mutex.ReleaseMutex(); }
        catch (ApplicationException) { }
        finally { mutex.Dispose(); }
    }

    private void QuitApp(Action? restart = null)
    {
        DeckleAppSource.Log.ShutdownRequested();
        var failures = new List<string>();
        void TryShutdown(string operation, Action action)
        {
            try { action(); }
            catch (Exception ex) { failures.Add($"{operation}: {ex.GetType().Name}: {ex.Message}"); }
        }

        TryShutdown("settings flush", () => Settings.SettingsService.Instance.Flush());
        TryShutdown("hotkeys dispose", () => _hotkeyManager?.Dispose());
        TryShutdown("tray dispose", () => _tray?.Dispose());
        TryShutdown("tray menu dispose", () => _trayMenu?.Dispose());
        // Before _messageHost: the signal's subclass + Raw Input sink sit on the
        // host's HWND, which _messageHost.Dispose destroys.
        TryShutdown("cursor signal dispose", () => _cursorSignal?.Dispose());
        TryShutdown("message host dispose", () => _messageHost?.Dispose());
        TryShutdown("overlay manager dispose", () => _overlayManager?.Dispose());
        TryShutdown("engine dispose", () => _engine?.Dispose());
        TryShutdown("speech engine dispose", () => _speechEngine?.Dispose());
        TryShutdown("trackpad shutdown", ShutdownTrackpad);
        TryShutdown("taskbar cover shutdown", ShutdownTaskbarCover);
        TryShutdown("precision scroll shutdown", ShutdownPrecisionScroll);
        TryShutdown("mouse wheel shutdown", ShutdownMouseWheel);
        TryShutdown("paragraph rewrite shutdown", ShutdownParagraphRewrite);
        TryShutdown("autocorrect shutdown", ShutdownAutocorrect);
        TryShutdown("anytype mcp shutdown", ShutdownAnytypeMcp);
        TryShutdown("ambient engine dispose", () =>
        {
            if (_ambientEngine is not null
                && !_ambientEngine.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Ambient engine teardown exceeded five seconds.");
            }
        });

        // Restart only after every resident service has stopped and the ownership
        // gate is released. Spawning earlier lets the successor reject itself
        // against this still-running process, then leaves no Deckle behind.
        ReleaseResidentOwnership();
        if (restart is not null)
            TryShutdown("restart spawn", restart);

        if (failures.Count > 0)
        {
            DeckleAppSource.Log.ShutdownWarning();
            DeckleAppSource.Log.ShutdownWarningDetail(string.Join(" | ", failures));
        }
        DeckleAppSource.Log.ShutdownCompleted();
        Environment.Exit(0);
    }

    public static void RestartApp(string? pageTag = null)
    {
        DeckleAppSource.Log.RestartRequested();
        try { Settings.SettingsService.Instance.Flush(); } catch { }

        if (Current is App app)
        {
            app.QuitApp(() =>
            {
                string? exePath = Environment.ProcessPath;
                if (exePath is null) return;
                string args = pageTag is not null
                    ? $"--settings \"{pageTag}\""
                    : "--settings";
                DeckleAppSource.Log.RestartSpawnNewProcess(exePath, args);
                System.Diagnostics.Process.Start(exePath, args);
            });
        }
    }

    public static void RestartViaShellExecute(string args = "")
    {
        DeckleAppSource.Log.PostBuildRestartRequested();
        try { Settings.SettingsService.Instance.Flush(); } catch { }

        if (Current is App app)
        {
            app.QuitApp(() =>
            {
                string? exePath = Environment.ProcessPath;
                if (exePath is null) return;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                };
                DeckleAppSource.Log.PostBuildShellExecute(exePath);
                try { System.Diagnostics.Process.Start(psi); }
                catch (Exception ex)
                {
                    DeckleAppSource.Log.PostBuildRelaunchFailed();
                    DeckleAppSource.Log.PostBuildRelaunchFailedDetail(ex.Message);
                }
            });
        }
    }

    private void RestartAppFromTray()
    {
        DeckleAppSource.Log.RestartFromTrayRequested();
        try { Settings.SettingsService.Instance.Flush(); } catch { }
        QuitApp(() =>
        {
            string? exePath = Environment.ProcessPath;
            if (exePath is null) return;
            DeckleAppSource.Log.RestartSpawnNewProcess(exePath, "");
            System.Diagnostics.Process.Start(exePath);
        });
    }
}

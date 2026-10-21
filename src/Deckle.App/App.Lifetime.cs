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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int steps = 0;
        int failed = 0;
        // One step failing never stops the next: each is logged where it
        // breaks, with its cause, and the teardown goes on to the exit.
        void TryShutdown(string step, Action action)
        {
            steps++;
            try { action(); }
            catch (Exception ex)
            {
                failed++;
                DeckleAppSource.Log.ShutdownStepFailed(step, ex.GetType().Name, ex.Message);
            }
        }

        TryShutdown("settings_flush", () => Settings.SettingsService.Instance.Flush());
        TryShutdown("hud_settings_flush", () => Hud.HudSettingsService.Instance.Flush());
        TryShutdown("hotkeys_dispose", () => _hotkeyManager?.Dispose());
        TryShutdown("tray_dispose", () => _tray?.Dispose());
        TryShutdown("tray_menu_dispose", () => _trayMenu?.Dispose());
        TryShutdown("tray_actions_dispose", () => _trayActions?.Dispose());
        // Before _messageHost: the signal's subclass + Raw Input sink sit on the
        // host's HWND, which _messageHost.Dispose destroys.
        TryShutdown("cursor_signal_dispose", () => _cursorSignal?.Dispose());
        TryShutdown("message_host_dispose", () => _messageHost?.Dispose());
        TryShutdown("overlay_manager_dispose", () => _overlayManager?.Dispose());
        TryShutdown("engine_dispose", () => _engine?.Dispose());
        TryShutdown("speech_engine_dispose", () => _speechEngine?.Dispose());
        TryShutdown("trackpad_shutdown", ShutdownTrackpad);
        TryShutdown("taskbar_cover_shutdown", ShutdownTaskbarCover);
        // After the cover, which reads it. UnhookWinEvent only succeeds on the UI
        // thread that started the watcher; from any other caller the hooks still
        // die with the process at Environment.Exit.
        TryShutdown("foreground_watcher_dispose", () => _foregroundWatcher?.Dispose());
        TryShutdown("precision_scroll_shutdown", ShutdownPrecisionScroll);
        TryShutdown("orchestration_shutdown", ShutdownOrchestration);
        TryShutdown("mouse_wheel_shutdown", ShutdownMouseWheel);
        TryShutdown("paragraph_rewrite_shutdown", ShutdownParagraphRewrite);
        TryShutdown("autocorrect_shutdown", ShutdownAutocorrect);
        TryShutdown("anytype_mcp_shutdown", ShutdownAnytypeMcp);
        TryShutdown("ambient_engine_dispose", () =>
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
            TryShutdown("restart_spawn", restart);

        DeckleAppSource.Log.ShutdownStepsDetail(steps, failed, stopwatch.ElapsedMilliseconds);
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

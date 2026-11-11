using Deckle.App;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Transcription;
using Deckle.Localization;

namespace Deckle.App;

public partial class App
{
    private const string ResidentMutexName = @"Local\Deckle.Resident";
    private const string SecondInstanceFileName = "second-instance.txt";
    private Mutex? _residentMutex;

    // Read by ReportCrashDumpCapture: ownership is taken before any sink
    // exists, so the line waits for the sinks.
    private static bool _residentMutexAbandoned;

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
            _residentMutexAbandoned = true;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return false;
        }

        _residentMutex = mutex;
        return true;
    }

    // A rejected second instance has no sink and may not install one: the
    // resident run holds the log open for writing, so the fact goes to a
    // small file of its own, beside the launcher's. The resident run copies it into
    // the log at its next start (ReportRejectedSecondInstances).
    private static void NoteSecondInstanceRejected() =>
        NoteFile.Append(
            Path.Combine(AppPaths.DiagnosticsDirectory, SecondInstanceFileName),
            $"pid={Environment.ProcessId} | another Deckle is resident, this one exits",
            DateTimeOffset.Now);

    private void ReleaseResidentOwnership()
    {
        Mutex? mutex = _residentMutex;
        _residentMutex = null;
        if (mutex is null) return;

        // ApplicationException: this thread does not own the mutex (QuitApp
        // reached from another thread). The handle closes either way.
        try { mutex.ReleaseMutex(); }
        catch (ApplicationException ex)
        {
            DeckleAppSource.Log.ResidentMutexReleaseFailed(ex.GetType().Name, ex.Message);
        }
        finally { mutex.Dispose(); }
    }

    // How long a quit waits for the work in flight before it gives up on it.
    //
    // The wait covers what a dictation does after the stop: the 0.4 s capture
    // tail, the transcription of the utterances still queued, then the optional
    // LLM rewrite, which on a cold start also loads the model. That is a few
    // seconds on a short take and tens of seconds on a long one with a rewrite.
    // 60 s leaves room for the long case. Beyond it, the person is more likely
    // to think Deckle froze than to wait on, and the recovery stores already
    // hold the text and the audio (see TranscriptionEngine.Shutdown.cs), so
    // giving up costs a "Recovered dictation" at the next launch, not the take.
    // The figure is an estimate from the pipeline's stages, not a measure.
    private static readonly TimeSpan QuitWorkBound = TimeSpan.FromSeconds(60);

    // Time the HUD keeps its "copied" confirmation on screen before
    // the process exits; without it the person never sees the take arrived.
    private static readonly TimeSpan QuitConfirmationHold = TimeSpan.FromMilliseconds(800);

    private int _quitStarted;

    // Read by the engine's status handler (App.Startup.Shell.cs) so the tray
    // tooltip keeps saying that Deckle closes when the work ends.
    private volatile bool _quitPending;

    private string QuitPendingStatus(string status) =>
        Deckle.Localization.Loc.Format("Status_QuitPending_Format", status);

    // Every path that ends the app arrives here: tray Quit, the three restarts,
    // the update and the data move. Work in flight is finished first (the
    // decision of 2026-11-07): a dictation is stopped as a hotkey release would
    // and delivers its text, then Deckle exits. The wait runs off the UI thread,
    // which stays free to paint the HUD; the teardown comes back to it, because
    // the resident mutex and the foreground hook belong to it.
    //
    // Judged not to need a wait, with the reason:
    //   - paragraph rewrite: only an offer the person has not accepted; nothing
    //     is written to their text before they accept it.
    //   - autocorrect: replaces a word at the keystroke, in milliseconds; the
    //     personal dictionary is persisted by its own dispose.
    //   - settings: written by the synchronous Flush below.
    //   - Anytype / MCP: a call cut short returns an error to the agent that
    //     made it, and Anytype's own backend commits or drops a write whole.
    //   - model downloads: written to a .partial file and renamed at the end
    //     (Downloader), run from a setup window the person watches.
    // A Windows session end does not reach this method (no WM_ENDSESSION
    // handler exists), and Windows grants seconds there, not a minute.
    private void QuitApp(Action? restart = null)
    {
        if (Interlocked.Exchange(ref _quitStarted, 1) != 0)
        {
            DeckleAppSource.Log.ShutdownRequestIgnored();
            return;
        }

        string workId = WorkId.Next();
        DeckleAppSource.Log.ShutdownRequested(workId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        TranscriptionEngine? engine = _engine;
        if (engine is null || !engine.IsBusy)
        {
            ExitApp(workId, stopwatch, restart);
            return;
        }

        bool wasRecording = engine.IsRecording;
        _quitPending = true;
        try
        {
            _tray?.UpdateStatus(
                QuitPendingStatus(Deckle.Localization.Loc.Get(
                    wasRecording ? "Status_Recording" : "Status_Transcribing")),
                isRecording: false);
            // The same acknowledgement as the hotkey release: the next engine
            // status only comes after the queued tail is decoded.
            if (wasRecording) _hudWindow?.SwitchToTranscribing();
        }
        catch (Exception ex)
        {
            DeckleAppSource.Log.ShutdownStepFailed("quit_pending_status", ex.GetType().Name, ex.Message);
        }

        var ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _ = Task.Run(() =>
        {
            DrainResult drained = DrainResult.TimedOut;
            try { drained = engine.FinishInFlightWork(workId, QuitWorkBound); }
            catch (Exception ex)
            {
                DeckleAppSource.Log.ShutdownStepFailed("work_drain", ex.GetType().Name, ex.Message);
            }
            if (drained == DrainResult.Finished) Thread.Sleep(QuitConfirmationHold);

            if (ui is null || !ui.TryEnqueue(() => ExitApp(workId, stopwatch, restart)))
                ExitApp(workId, stopwatch, restart);
        });
    }

    private void ExitApp(string workId, System.Diagnostics.Stopwatch stopwatch, Action? restart)
    {
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
        TryShutdown("update_checks_stop", StopUpdateChecks);
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
        TryShutdown("taskbar_shutdown", ShutdownTaskbar);
        // After the cover, which reads it. UnhookWinEvent only succeeds on the UI
        // thread that started the watcher; from any other caller the hooks still
        // die with the process at Environment.Exit.
        TryShutdown("foreground_watcher_dispose", () => _foregroundWatcher?.Dispose());
        TryShutdown("precision_scroll_shutdown", ShutdownPrecisionScroll);
        TryShutdown("orchestration_shutdown", ShutdownOrchestration);
        TryShutdown("taskbar_windows_key_shutdown", ShutdownTaskbarWindowsKey);
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
        // The run mark goes first: the successor writes its own once it owns
        // the gate, and must not find this run's mark still there.
        MarkRunEnded();
        ReleaseResidentOwnership();
        if (restart is not null)
        {
            // Not a TryShutdown step: a successor that cannot start leaves the
            // user with no Deckle, which is an Error that says so, where a
            // failed teardown step is only a Warning.
            steps++;
            try { restart(); }
            catch (Exception ex)
            {
                failed++;
                DeckleAppSource.Log.RelaunchFailed(ex.GetType().Name, ex.Message);
            }
        }

        DeckleAppSource.Log.ShutdownCompleted(workId, steps, failed, stopwatch.ElapsedMilliseconds);
        Environment.Exit(0);
    }

    public static void RestartApp(string? pageTag = null)
    {
        DeckleAppSource.Log.RestartRequested();
        FlushSettingsBeforeRestart();

        if (Current is App app)
        {
            app.QuitApp(() =>
            {
                string? exePath = Environment.ProcessPath;
                if (exePath is null)
                {
                    DeckleAppSource.Log.RestartSpawnSkipped("process_path_unknown");
                    return;
                }
                string args = pageTag is not null
                    ? $"--settings \"{pageTag}\""
                    : "--settings";
                DeckleAppSource.Log.RestartSpawnNewProcess(exePath, args);
                var child = System.Diagnostics.Process.Start(exePath, args);
                DeckleAppSource.Log.RestartSpawned(child?.Id ?? 0);
            });
        }
    }

    public static void RestartViaShellExecute(string args = "")
    {
        DeckleAppSource.Log.PostBuildRestartRequested();
        FlushSettingsBeforeRestart();

        if (Current is App app)
        {
            app.QuitApp(() =>
            {
                string? exePath = Environment.ProcessPath;
                if (exePath is null)
                {
                    DeckleAppSource.Log.RestartSpawnSkipped("process_path_unknown");
                    return;
                }
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                };
                DeckleAppSource.Log.PostBuildShellExecute(exePath);
                // A start that throws reaches QuitApp, which reports it for
                // every restart path alike.
                var child = System.Diagnostics.Process.Start(psi);
                DeckleAppSource.Log.RestartSpawned(child?.Id ?? 0);
            });
        }
    }

    private void RestartAppFromTray()
    {
        DeckleAppSource.Log.RestartFromTrayRequested();
        FlushSettingsBeforeRestart();
        QuitApp(() =>
        {
            string? exePath = Environment.ProcessPath;
            if (exePath is null)
            {
                DeckleAppSource.Log.RestartSpawnSkipped("process_path_unknown");
                return;
            }
            DeckleAppSource.Log.RestartSpawnNewProcess(exePath, "");
            var child = System.Diagnostics.Process.Start(exePath);
            DeckleAppSource.Log.RestartSpawned(child?.Id ?? 0);
        });
    }

    // The restart goes on regardless; a failed flush only means the last
    // settings change may not survive it, which the Warning says.
    private static void FlushSettingsBeforeRestart()
    {
        try { Settings.SettingsService.Instance.Flush(); }
        catch (Exception ex)
        {
            DeckleAppSource.Log.RestartSettingsFlushFailed(ex.GetType().Name, ex.Message);
        }
    }
}

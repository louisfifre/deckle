using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Deckle.Hud;
using Deckle.Lighting.Ambient;
using Deckle.Modules;
using Deckle.Playground;
using Deckle.Setup;
using Deckle.Shell;
using Deckle.Shell.TaskbarCover;
using Deckle.Shell.TrayMenu;
using Deckle.Speech;
using Deckle.Transcription;
using Deckle.Transcription.Whisper;

namespace Deckle.App;

public partial class App
{
    private void ApplyStartupPreferencesAndArguments(StartupContext context)
    {
        ApplyTheme(Settings.SettingsService.Instance.Current.Appearance.Theme);

        // So the first recording reflects the user's calibration, not the defaults.
        ApplyLevelWindow(Audio.CaptureSettingsService.Instance.Current.LevelWindow);

        var cliArgs = Environment.GetCommandLineArgs();

        HandleDataRootCleanup(cliArgs);

        int settingsIdx = Array.IndexOf(cliArgs, "--settings");
        if (settingsIdx >= 0)
        {
            string? pageTag = settingsIdx + 1 < cliArgs.Length
                ? cliArgs[settingsIdx + 1]
                : null;
            DeckleAppSource.Log.CmdLineSettingsFlag(pageTag ?? "(default)");
            ShowSettingsWindowLazy(pageTag);
        }

        // Diagnostic-only repro of the HUD z-order bug: relaunches like
        // --post-build, into a bounded self-test that shows the HUD once, then quits.
        int postBuildHudZOrderSelfTestIdx = Array.IndexOf(cliArgs, "--post-build-hud-zorder-selftest");
        if (postBuildHudZOrderSelfTestIdx >= 0)
        {
            DeckleAppSource.Log.CmdLinePostBuildFlag();
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var timer = dq.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(800);
            timer.IsRepeating = false;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                RestartViaShellExecute("--hud-zorder-selftest");
            };
            timer.Start();
        }

        int hudZOrderSelfTestIdx = Array.IndexOf(cliArgs, "--hud-zorder-selftest");
        if (hudZOrderSelfTestIdx >= 0)
        {
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var showTimer = dq.CreateTimer();
            showTimer.Interval = TimeSpan.FromMilliseconds(1200);
            showTimer.IsRepeating = false;
            showTimer.Tick += (s, e) =>
            {
                showTimer.Stop();
                _hudWindow!.ShowRecording();

                var quitTimer = dq.CreateTimer();
                quitTimer.Interval = TimeSpan.FromMilliseconds(2500);
                quitTimer.IsRepeating = false;
                quitTimer.Tick += (s2, e2) =>
                {
                    quitTimer.Stop();
                    _hudWindow!.Hide();
                    QuitApp();
                };
                quitTimer.Start();
            };
            showTimer.Start();
        }

        // Passed by scripts/commands/build-run.ps1. The first launch right after
        // MSBuild can inherit a degraded foreground state that makes Windows
        // defer WS_EX_TOPMOST on the HUD, so the first recording shows it behind
        // every other window. A relaunch through ShellExecute starts clean.
        int postBuildIdx = Array.IndexOf(cliArgs, "--post-build");
        if (postBuildIdx >= 0)
        {
            DeckleAppSource.Log.CmdLinePostBuildFlag();
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var timer = dq.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(800);
            timer.IsRepeating = false;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                RestartViaShellExecute();
            };
            timer.Start();
        }

        context.Complete();

        // Queued behind the first frame, so the tray is up before the window
        // opens. A post-build process hands over to its relaunch, which runs
        // this same check; offering here too would open the window twice.
        if (_speechRepairReason is { } repairReason && postBuildIdx < 0 && postBuildHudZOrderSelfTestIdx < 0)
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
                .TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => OfferSpeechRepair(repairReason));
    }
}

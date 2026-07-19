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
        // Apply saved theme (System/Light/Dark).
        ApplyTheme(Settings.SettingsService.Instance.Current.Appearance.Theme);

        // Apply persisted level window (MinDbfs / MaxDbfs / DbfsCurveExponent)
        // into AudioLevelMapper so the first Recording reflects the user's
        // calibration without a restart-from-defaults round-trip.
        ApplyLevelWindow(Audio.CaptureSettingsService.Instance.Current.LevelWindow);

        // If launched with --settings (restart from Settings), automatically
        // reopen the Settings window on the right page.
        var cliArgs = Environment.GetCommandLineArgs();

        // A relocated data root leaves its origin behind for this process to
        // remove — the transaction's last step, run only once the new root is
        // live and nothing holds the old tree. Guarded in App.Relocate.cs.
        HandleDataRootCleanup(cliArgs);

        int settingsIdx = Array.IndexOf(cliArgs, "--settings");
        if (settingsIdx >= 0)
        {
            string? pageTag = settingsIdx + 1 < cliArgs.Length
                ? cliArgs[settingsIdx + 1]
                : null;
            DeckleAppSource.Log.CmdLineSettingsFlag(pageTag ?? "(default)");
            // Lazy path: creates the window + shows it on the requested page.
            // Indistinguishable from the tray path when the user opens
            // Settings for the first time.
            ShowSettingsWindowLazy(pageTag);
        }

        // Diagnostic-only repro path for the HUD z-order bug. It follows the
        // same shape as the post-build workaround, but relaunches into a
        // bounded self-test that triggers the first real HUD show, then quits.
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

        // If launched with --post-build (set by scripts/lib/build-run.ps1),
        // schedule a one-shot self-restart via ShellExecute. The first
        // launch right after MSBuild occasionally inherits a degraded
        // foreground state that makes Windows defer WS_EX_TOPMOST on the
        // HUD, so the first recording shows the HUD behind every other
        // window. Relaunching ourselves through cmd /c start gives the
        // new process a clean foreground state.
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
    }
}

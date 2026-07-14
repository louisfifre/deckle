using System.Diagnostics;

using Deckle.Core;
using Deckle.Install;
using Deckle.Modules;
using Deckle.Notifications;
using Deckle.Setup;

namespace Deckle.App;

// ── Update mode ──────────────────────────────────────────────────────────────
//
// The in-app updater's two App-side pieces, mirroring App.Install.cs.
//
// The running app hosts the silent check (WireUpdater: boot + daily, gated by
// the Settings opt-out) and the explicit flow (StartUpdateFlow: the download
// window; on its success the app exits, releasing the binaries). The NEW
// payload's process, spawned by the download page from its temp extraction,
// enters through --update-apply and runs DeployPage over the live install —
// the same two-process split as the install chain, with the roles reversed.
public partial class App
{
    // ── Silent check + Settings hooks (runs in the normal app) ──────────────

    private Deckle.Setup.SetupWindow? _updateWindow;

    private void WireUpdater()
    {
        Settings.SettingsHost.GetAppVersion = CurrentDisplayVersion;
        Settings.SettingsHost.GetAvailableUpdateVersion = () => UpdateService.Available?.Version;
        Settings.SettingsHost.StartUpdate = StartUpdateFlow;

        // Dev builds never see updates: the check is gated on running the
        // registered install's own exe, so a worktree launch stops here.
        if (!UpdateService.IsInstalledLaunch()) return;

        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _ = Task.Run(async () =>
        {
            // One toast per discovered version per process lifetime — a tray
            // app can run for weeks, and re-toasting a declined offer daily
            // is nagging, not surfacing. The offer stays parked in Settings.
            var prompted = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                try
                {
                    if (Settings.SettingsService.Instance.Current.Updates.AutoCheckEnabled
                        && await UpdateService.CheckAsync().ConfigureAwait(false) is { } update
                        && prompted.Add(update.Version)
                        && NotificationDispatcher.Instance is { } dispatcher)
                    {
                        var response = await dispatcher.PromptAsync(
                            SetupNotifications.UpdateAvailable, new object?[] { update.Version })
                            .ConfigureAwait(false);
                        if (response is { ActionId: "install" }
                            or { ActionId: NotificationResponse.BodyActionId })
                            dq.TryEnqueue(StartUpdateFlow);
                    }
                }
                catch
                {
                    // Never let the background check take the app down — the
                    // service and the dispatcher already narrate their failures.
                }
                await Task.Delay(TimeSpan.FromHours(24)).ConfigureAwait(false);
            }
        });
    }

    // Opens the update window on the parked release. Idempotent while one is
    // already open — the toast and the Settings button can both fire.
    private void StartUpdateFlow()
    {
        if (_updateWindow is not null)
        {
            _updateWindow.Activate();
            return;
        }
        if (UpdateService.Available is not { } update) return;
        if (UninstallEntry.Read() is not { } existing) return;

        var context = new SetupContext
        {
            UpdateMode       = true,
            PendingUpdate    = update,
            InstallDirectory = existing.InstallDir,
        };
        var setup = new Deckle.Setup.SetupWindow(context);
        _updateWindow = setup;
        setup.Body.Navigate(typeof(UpdateDownloadPage), setup);
        setup.Activate();
        _ = CompleteUpdateAsync(setup);
    }

    private async Task CompleteUpdateAsync(Deckle.Setup.SetupWindow setup)
    {
        bool ok = await setup.Completion;
        _updateWindow = null;
        // The download page spawned the new payload's --update-apply, now
        // waiting on the running-process gate — this process exits to free
        // the binaries. A cancel or failure leaves the app running untouched.
        if (ok) QuitApp();
    }

    // The running build's display version — the payload's bare number, the
    // same form the Installed-apps entry stores (informational +commit suffix
    // dropped).
    private static string CurrentDisplayVersion()
    {
        if (Environment.ProcessPath is not { } exe) return "";
        string? version = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
        if (string.IsNullOrWhiteSpace(version)) return "";
        int plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    // ── --update-apply (runs in the NEW payload, from the temp extraction) ──

    private async Task RunUpdateApplyAsync(string[] args)
    {
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        AppModules.RegisterAll();

        string? target  = ArgumentValue(args, "--target");
        string? cleanup = ArgumentValue(args, "--cleanup");
        DeckleAppSource.Log.InstallModeEntered();
        DeckleAppSource.Log.InstallModeEnteredDetail("update-apply", "", cleanup ?? "", "");

        // No live install to update over — a bare exit; this process was
        // spawned programmatically, there is no user journey to land on.
        if (target is null || !File.Exists(Path.Combine(target, "Deckle.exe")))
            Environment.Exit(1);

        // The registered uninstaller already lives in the install folder;
        // pointing StubPath at it makes DeployPage.Integrate refresh the
        // Installed-apps entry (new version, same uninstaller). Deploy's
        // pre-copy clean spares that file, and its PathsEqual guard skips
        // the self-copy.
        string uninstaller = Path.Combine(target!, "Deckle-Installer.exe");

        var context = new SetupContext
        {
            InstallMode      = true,
            UpdateMode       = true,
            InstallDirectory = target!,
            DataDirectory    = UserEnvironment.GetDataRoot() ?? InstallPaths.DefaultDataDir,
            StubPath         = File.Exists(uninstaller) ? uninstaller : null,
            CleanupDirectory = cleanup,
            // Presence as recorded — an update never changes the module choice.
            // SelectedModel stays null: the continuation resolves the configured
            // model from settings, so nothing new is downloaded by default.
            SelectedModules  = ModulePresence.Choice
                ?? ModuleRegistry.Modules.Select(m => m.Id).ToHashSet(StringComparer.Ordinal),
        };

        var setup = new Deckle.Setup.SetupWindow(context);
        setup.Body.Navigate(typeof(DeployPage), setup);
        setup.Activate();

        // Deploy spawned the updated install's --install-continue on success
        // (provision check, temp cleanup, plain relaunch); either way this
        // temp process is done.
        await setup.Completion;
        Environment.Exit(0);
    }
}

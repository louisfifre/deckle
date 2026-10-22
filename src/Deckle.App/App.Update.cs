using System.Diagnostics;
using System.Reflection;

using Deckle.Core;
using Deckle.Install;
using Deckle.Modules;
using Deckle.Notifications;
using Deckle.Setup;

namespace Deckle.App;

// The running app hosts the silent check and the explicit flow, and exits on the
// flow's success to release the binaries. The new payload's process, spawned by
// the download page from its temp extraction, enters through --update-apply and
// runs DeployPage over the live install: the install chain's two-process split
// with the roles reversed.
public partial class App
{
    private Deckle.Setup.SetupWindow? _updateWindow;

    // Cancelled by QuitApp, so the loop's last line says it stopped rather
    // than dying unannounced with the process.
    private CancellationTokenSource? _updateChecksCts;

    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private void WireUpdater()
    {
        Settings.SettingsHost.GetAppVersion = CurrentDisplayVersion;
        Settings.SettingsHost.GetAppStage = CurrentStage;
        Settings.SettingsHost.GetAvailableUpdateVersion = () => UpdateService.Available?.Version;
        Settings.SettingsHost.StartUpdate = StartUpdateFlow;

        // Dev builds never see updates: the check is gated on running the
        // registered install's own exe, so a worktree launch stops here.
        if (!UpdateService.IsInstalledLaunch()) return;

        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var cts = new CancellationTokenSource();
        _updateChecksCts = cts;
        CancellationToken ct = cts.Token;
        DeckleAppSource.Log.UpdateChecksStarted((int)UpdateCheckInterval.TotalHours);
        _ = Task.Run(async () =>
        {
            // One toast per discovered version per process lifetime — a tray
            // app can run for weeks, and re-toasting a declined offer daily
            // is nagging, not surfacing. The offer stays parked in Settings.
            var prompted = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                while (true)
                {
                    try
                    {
                        if (!Settings.SettingsService.Instance.Current.Updates.AutoCheckEnabled)
                        {
                            DeckleAppSource.Log.UpdateCheckOutcome("disabled", "", "", "");
                        }
                        else if (await UpdateService.CheckAsync().ConfigureAwait(false) is not { } update)
                        {
                            DeckleAppSource.Log.UpdateCheckOutcome("up_to_date", "", "", "");
                        }
                        else if (!prompted.Add(update.Version))
                        {
                            DeckleAppSource.Log.UpdateCheckOutcome("already_prompted", update.Version, "", "");
                        }
                        else
                        {
                            DeckleAppSource.Log.UpdateCheckOutcome("available", update.Version, "", "");
                            if (NotificationDispatcher.Instance is { } dispatcher)
                            {
                                var response = await dispatcher.PromptAsync(
                                    SetupNotifications.UpdateAvailable, new object?[] { update.Version })
                                    .ConfigureAwait(false);
                                if (response is { ActionId: "install" }
                                    or { ActionId: NotificationResponse.BodyActionId })
                                    dq.TryEnqueue(StartUpdateFlow);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Never let the background check take the app down — the
                        // service and the dispatcher already narrate their failures.
                        DeckleAppSource.Log.UpdateCheckOutcome("failed", "", ex.GetType().Name, ex.Message);
                    }
                    await Task.Delay(UpdateCheckInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                DeckleAppSource.Log.UpdateChecksStopped("shutdown");
            }
        });
    }

    private void StopUpdateChecks()
    {
        CancellationTokenSource? cts = _updateChecksCts;
        _updateChecksCts = null;
        cts?.Cancel();
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
        if (UpdateService.Available is not { } update)
        {
            DeckleAppSource.Log.UpdateFlowRefused("no_pending_update");
            return;
        }
        if (UninstallEntry.Read() is not { } existing)
        {
            DeckleAppSource.Log.UpdateFlowRefused("install_not_registered");
            return;
        }

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
        DeckleAppSource.Log.UpdateFlowStarted(update.Version);
        _ = CompleteUpdateAsync(setup);
    }

    private async Task CompleteUpdateAsync(Deckle.Setup.SetupWindow setup)
    {
        var stopwatch = Stopwatch.StartNew();
        bool ok;
        try
        {
            ok = await setup.Completion;
        }
        catch (Exception ex)
        {
            // Unawaited by StartUpdateFlow: without this the failure would only
            // surface later as an unobserved task, and the window slot would
            // stay taken so the next request only re-activates a dead window.
            _updateWindow = null;
            DeckleAppSource.Log.UpdateFlowFailed(ex.GetType().Name, ex.Message, stopwatch.ElapsedMilliseconds);
            return;
        }
        _updateWindow = null;
        DeckleAppSource.Log.UpdateFlowFinished(ok ? "completed" : "cancelled", stopwatch.ElapsedMilliseconds);
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

    // The release stage baked in by the csproj's DeckleStage property, through
    // an AssemblyMetadata item. Read from the attribute rather than parsed out
    // of the version string, so the version stays the bare number everywhere
    // (tag, update check, Installed-apps entry) and the stage is one word.
    private static string CurrentStage()
    {
        return typeof(App).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static a => a.Key == "Stage")?.Value ?? "";
    }

    // Runs in the new payload, from its temp extraction.
    private async Task RunUpdateApplyAsync(string[] args)
    {
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        AppModules.RegisterAll();

        string? target  = ArgumentValue(args, "--target");
        string? cleanup = ArgumentValue(args, "--cleanup");
        int? predecessor = int.TryParse(
            ArgumentValue(args, "--wait-pid"),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out int parsedPredecessor)
                ? parsedPredecessor
                : null;
        DeckleAppSource.Log.InstallModeEntered();
        DeckleAppSource.Log.InstallModeEnteredDetail(UpdateApplyMode, "", cleanup ?? "", "");

        // No live install to update over — a bare exit; this process was
        // spawned programmatically, there is no user journey to land on.
        if (target is null || !File.Exists(Path.Combine(target, "Deckle.exe")))
        {
            ReportTransactionalModeExit(
                UpdateApplyMode, "refused", target is null ? "target_missing" : "target_not_an_install");
            Environment.Exit(1);
        }

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
            PredecessorProcessId = predecessor,
            // Presence as recorded — an update never changes the module choice.
            // SelectedModel stays null: the continuation resolves the configured
            // model from settings, so nothing new is downloaded by default.
            SelectedModules  = ModulePresence.Choice
                ?? ModuleRegistry.Modules.Select(m => m.Id).ToHashSet(StringComparer.Ordinal),
        };

        var setup = new Deckle.Setup.SetupWindow(context);
        setup.Body.Navigate(typeof(DeployPage), setup);
        setup.Activate();

        // On success Deploy spawned the updated install's --install-continue;
        // either way this temp process is done.
        bool ok = await setup.Completion;
        ReportTransactionalModeExit(UpdateApplyMode, ok ? "completed" : "cancelled", "none");
        Environment.Exit(0);
    }
}

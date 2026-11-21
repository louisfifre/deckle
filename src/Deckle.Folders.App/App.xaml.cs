using Deckle.Contributions;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Hosting;
using Microsoft.UI.Xaml;

namespace Deckle.Folders.App;

// ── Deckle Folders ───────────────────────────────────────────────────────────────
//
// The Deckle Folders window as an app of its own, built on the core libraries and the
// host kit (Deckle.Hosting) the published Deckle is built on, so the
// maintainer can try it without the published Deckle carrying any of it.
//
// What it is: one window, on the folders it adds itself. The app owns its identity
// ("folders": Program.cs), hence its own data root (<suite root>\apps\folders), its
// own log folder (<suite root>\diagnostics\folders) and its own resident mutex,
// so it runs beside the maintainer's Deckle and touches none of its files.
// Closing the window ends the app.
//
// What it does not have yet, on purpose, for a first cut: a tray icon, a
// crash supervisor, the hand-off of a prod update, a Settings window (the
// theme follows the system) and the Log window (the log is read from the
// files). Started by hand, from its folder.
public partial class App : Application
{
    // Long enough for a healthy disk to drain a full queue, short enough that
    // a crash never looks like a hang.
    private static readonly TimeSpan FlushDeadline = TimeSpan.FromSeconds(5);

    private const string SecondInstanceFileName = "second-instance.txt";

    private const string BuildConfiguration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    // The crash detail is Verbose, so a level predicate alone would split a
    // crash across files; the log allowlists it by name.
    private static readonly HashSet<string> CrashCompanions = [];

    private ResidentInstance? _resident;
    private ContributionHost? _host;
    private bool _diagnosticsStarted;

    public App()
    {
        InitializeComponent();

        // As in the published Deckle, the UI framework's net marks the
        // exception handled and the app goes on, degraded: it is reported as
        // one incident per exception type and site. The AppDomain net is the
        // one that ends the process, and it keeps its Error.
        this.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            ReportSurvived(e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportUnhandled("app_domain", e.ExceptionObject as Exception);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!_diagnosticsStarted) return;
            DeckleFoldersAppSource.Log.AppEnded(DeckleEventSource.SessionId, Environment.ProcessId);
            DiagnosticsHost.Flush(FlushDeadline);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cli = Environment.GetCommandLineArgs();

        // The self-check is a separate mode, before the ownership below: it
        // runs beside a resident Deckle Folders, on a data root of its own.
        if (IsSelfCheckMode(cli))
        {
            _ = RunSelfCheckModeAsync(cli);
            return;
        }

        // One copy at a time. The copy turned away has no sink (the resident
        // one holds the log open), so it leaves a note beside it.
        _resident = ResidentInstance.TryAcquire(AppIdentity.Current, out string? residentFailure);
        if (_resident is null)
        {
            NoteFile.Append(
                Path.Combine(AppPaths.DiagnosticsDirectory, SecondInstanceFileName),
                $"pid={Environment.ProcessId} | {(residentFailure is null ? "Deckle Folders is already running" : $"the resident gate could not be created ({residentFailure})")}, this copy exits",
                DateTimeOffset.Now);
            Exit();
            return;
        }

        StartDiagnostics();
        ContributionHost host = EnsureHost();
        host.Compose();

        Window window = host.Ensure("folders");
        window.Closed += (_, _) => Quit();
        host.Open("folders");
    }

    // The local log files and the boot record, before anything else logs.
    private void StartDiagnostics()
    {
        PathRedaction.AddRoot(PathRedaction.DataRootToken, AppPaths.UserDataRoot);
        DiagnosticsHost.InitializeLocalSinks(AppPaths.DiagnosticsDirectory, CrashCompanions);
        _diagnosticsStarted = true;
        DeckleFoldersAppSource.Log.AppStarting(
            ProcessVersion.Display(), BuildConfiguration, DeckleEventSource.SessionId, Environment.ProcessId);
        LogDataMigration();
        DiagnosticsHost.CompleteBoot();
    }

    // The one-time move of the old data folder (FoldersDataMigration), told
    // now that the log is open. Folders are named from the suite root.
    private static void LogDataMigration()
    {
        FoldersDataMigrationResult result = Program.DataMigration;
        string oldFolder = Path.Combine(AppRoots.AppsFolderName, FoldersDataMigration.OldAppId);
        string newFolder = Path.Combine(AppRoots.AppsFolderName, FoldersDataMigration.NewAppId);
        switch (result.Outcome)
        {
            case FoldersDataMigrationOutcome.Moved:
                DeckleFoldersAppSource.Log.DataRootMoved(oldFolder, newFolder);
                break;
            case FoldersDataMigrationOutcome.MovedModuleFolderKept:
                DeckleFoldersAppSource.Log.DataRootMoved(oldFolder, newFolder);
                DeckleFoldersAppSource.Log.ModuleFolderKept(
                    Path.Combine(newFolder, "modules", FoldersDataMigration.OldAppId),
                    Path.Combine(newFolder, "modules", FoldersDataMigration.NewAppId));
                break;
            case FoldersDataMigrationOutcome.MovedModuleFolderNotRenamed:
                DeckleFoldersAppSource.Log.DataRootMoved(oldFolder, newFolder);
                DeckleFoldersAppSource.Log.ModuleFolderRenameFailed(
                    Path.Combine(newFolder, "modules", FoldersDataMigration.OldAppId),
                    Path.Combine(newFolder, "modules", FoldersDataMigration.NewAppId),
                    result.Error?.GetType().Name ?? "(unknown)",
                    PathRedaction.Redact(result.Error?.Message ?? "(no message)"));
                break;
            case FoldersDataMigrationOutcome.BothExist:
                DeckleFoldersAppSource.Log.DataRootNotMoved(oldFolder, newFolder);
                break;
            case FoldersDataMigrationOutcome.Failed:
                DeckleFoldersAppSource.Log.DataRootMoveFailed(
                    oldFolder, newFolder,
                    result.Error?.GetType().Name ?? "(unknown)",
                    PathRedaction.Redact(result.Error?.Message ?? "(no message)"));
                break;
        }
    }

    // The composition becomes a host on first use: a wrong composition refuses
    // the start there, before any window exists.
    private ContributionHost EnsureHost() =>
        _host ??= new ContributionHost(Composition.All, new ContributionServices());

    private void Quit()
    {
        _resident?.Release();
        _resident = null;
        Exit();
    }

    private static readonly SurvivedExceptionLedger SurvivedLedger = new();

    private void ReportSurvived(Exception exception)
    {
        if (!_diagnosticsStarted) return;
        SurvivedException seen = SurvivedLedger.Note(SurvivedExceptionOrigin.UiThread, exception);
        if (!seen.OpensIncident)
        {
            DeckleFoldersAppSource.Log.SurvivedExceptionRepeated(seen.Origin, seen.Type, seen.Site, seen.Occurrences);
            return;
        }
        // An exception message can quote a path under the user's profile.
        DeckleFoldersAppSource.Log.SurvivedException(
            seen.Origin, seen.Type, seen.Site, PathRedaction.Redact(seen.Message));
        DeckleFoldersAppSource.Log.SurvivedStack(PathRedaction.Redact(exception.ToString()));
    }

    private void ReportUnhandled(string origin, Exception? ex)
    {
        if (!_diagnosticsStarted) return;
        DeckleFoldersAppSource.Log.UnhandledException(
            origin, ex?.GetType().Name ?? "(unknown)", ex?.Message ?? "(no message)");
        DiagnosticsHost.Flush(FlushDeadline);
    }

    // What the host runner asks of this application: the window mechanics of
    // the kit, over this app's own data root. There is no Settings window, no
    // module presence choice and no developer mode here, so a window of the
    // composition opens whatever those would say.
    private sealed class ContributionServices : IContributionHostServices
    {
        public Window CreateOrLog(string name, Func<Window> create)
            => WindowLifecycle.CreateOrLog(name, create);

        public void Place(Window window, string name)
        {
            WindowPlacement.Restore(window, name);
            window.AppWindow.Closing += (_, _) => WindowPlacement.Save(window, name);
        }

        public void ApplyTheme(Window window) => WindowTheme.Apply(window);

        public void WhenClosed(Window window, Action closed)
            => window.Closed += (_, _) => closed();

        public void WindowOpened(string name) { }

        public string ModuleDirectory(string moduleId) => AppPaths.GetModuleDirectory(moduleId);

        public bool IsModulePresent(string moduleId) => true;

        public bool DeveloperModeEnabled => true;

        public void OpenSettingsPage(string pageTag) { }
    }
}

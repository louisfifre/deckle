using Deckle.Contributions;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Hosting;
using Microsoft.UI.Xaml;

namespace Deckle.Projects.App;

// ── Deckle Projects ───────────────────────────────────────────────────────────
//
// The Projects window as an app of its own, built on the core libraries and
// the host kit (Deckle.Hosting) the published Deckle is built on, as Deckle
// Folders and Deckle Mail are, so it starts from a Start menu shortcut and the
// published Deckle carries none of it.
//
// What it is: one window. The app owns its identity ("projects": Program.cs),
// hence its own data root (<suite root>\apps\projects), its own log folder
// (<suite root>\diagnostics\projects) and its own resident mutex, so it runs
// beside the maintainer's Deckle and touches none of its files. On its first
// start it copies the projects the published Deckle kept (ProjectsDataCopy).
//
// Closing the window ends the app, as in Deckle Folders and Deckle Mail. An
// action that runs goes on in its terminal: the spec's rule that closing the
// window never stops an action holds. What ends is the app following it: the
// running state, "Show terminal" and the result line are not there when the
// app opens again, the way the published Deckle already lost them when it
// closed (spec, Open 2, built today: A). Keeping the process alive with no
// window instead would leave a program the person cannot see nor close, with
// no tray icon to reach it, and a second start turned away by the resident
// gate.
//
// What it does not have yet, on purpose, for a first cut: a tray icon, a
// crash supervisor, the hand-off of a prod update, a Settings window (the
// theme follows the system; the terminal is the one terminal.json names,
// Windows Terminal by default) and the Log window (the log is read from the
// files). Started from its Start menu shortcut or its folder.
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
            DeckleProjectsAppSource.Log.AppEnded(DeckleEventSource.SessionId, Environment.ProcessId);
            DiagnosticsHost.Flush(FlushDeadline);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cli = Environment.GetCommandLineArgs();

        // The self-check is a separate mode, before the ownership below: it
        // runs beside a resident Deckle Projects, on a data root of its own.
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
                $"pid={Environment.ProcessId} | {(residentFailure is null ? "Deckle Projects is already running" : $"the resident gate could not be created ({residentFailure})")}, this copy exits",
                DateTimeOffset.Now);
            Exit();
            return;
        }

        StartDiagnostics();
        ContributionHost host = EnsureHost();
        host.Compose();

        Window window = host.Ensure("projects");
        window.Closed += (_, _) => Quit();
        host.Open("projects");
    }

    // The local log files and the boot record, before anything else logs.
    private void StartDiagnostics()
    {
        PathRedaction.AddRoot(PathRedaction.DataRootToken, AppPaths.UserDataRoot);
        DiagnosticsHost.InitializeLocalSinks(AppPaths.DiagnosticsDirectory, CrashCompanions);
        _diagnosticsStarted = true;
        DeckleProjectsAppSource.Log.AppStarting(
            ProcessVersion.Display(), BuildConfiguration, DeckleEventSource.SessionId, Environment.ProcessId);
        LogDataCopy();
        DiagnosticsHost.CompleteBoot();
    }

    private static void LogDataCopy()
    {
        ProjectsDataCopyResult result = Program.DataCopy;
        switch (result.Outcome)
        {
            case ProjectsDataCopyOutcome.Copied:
                DeckleProjectsAppSource.Log.DataCopied(result.From, result.To, result.Files);
                break;
            case ProjectsDataCopyOutcome.Failed:
                DeckleProjectsAppSource.Log.DataCopyFailed(
                    result.From, result.To,
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
        int runs = Composition.Projects.RunsGoingOn;
        if (runs > 0) DeckleProjectsAppSource.Log.RunsOutliveApp(runs);
        Composition.Projects.ReleaseWorkspace();
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
            DeckleProjectsAppSource.Log.SurvivedExceptionRepeated(seen.Origin, seen.Type, seen.Site, seen.Occurrences);
            return;
        }
        // An exception message can quote a path under the user's profile.
        DeckleProjectsAppSource.Log.SurvivedException(
            seen.Origin, seen.Type, seen.Site, PathRedaction.Redact(seen.Message));
        DeckleProjectsAppSource.Log.SurvivedStack(PathRedaction.Redact(exception.ToString()));
    }

    private void ReportUnhandled(string origin, Exception? ex)
    {
        if (!_diagnosticsStarted) return;
        DeckleProjectsAppSource.Log.UnhandledException(
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

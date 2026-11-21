using Deckle.Contributions;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Hosting;
using Deckle.Hosting.SelfCheck;
using Deckle.Vision;
using static Deckle.Hosting.SelfCheck.SelfCheckProcess;

namespace Deckle.Folders.App;

// ── Self-check: the app opened for real, beside the resident one ──────────────
//
// `DeckleFolders.exe --self-check <output-folder> [--only <target>]` is the same
// separate mode as the published Deckle's (src/Deckle.App/App.SelfCheck.cs),
// on the same harness (Deckle.Hosting.SelfCheck): it refuses to run unless
// DECKLE_DATA_ROOT names a root of its own, opens Deckle Folders window off screen,
// walks its drawn states (FoldersSelfCheck), writes report.json and exits 0 when
// all is well, 1 otherwise, 2 when refused. `--only folders/<view>` keeps the run
// to one view. scripts/self-check.ps1 reads the same report.
public partial class App
{
    internal const string SelfCheckFlag = "--self-check";

    internal static bool IsSelfCheckMode(string[] args) => Array.IndexOf(args, SelfCheckFlag) >= 0;

    // The windows a target may name: the composition's.
    private static readonly SelfCheckWindows Catalog = new(
        Windows: [.. Composition.Walks.Select(w => w.WindowName)],
        WithViews: [.. Composition.Walks.Where(w => w.HasViews).Select(w => w.WindowName)]);

    private static readonly string[] ComposedBySelfCheck =
    [
        "diagnostics: local sinks (log.jsonl, setup.jsonl), written under the run's own root",
        "windows: Deckle Folders window, created by the same lazy path the app opens it by",
        "folders: a sample folder generated under the run's own root, added as the picker adds one",
    ];

    private static readonly SelfCheckLeftOut[] LeftOutOfSelfCheck =
    [
        new("resident ownership (Local\\Deckle.Folders.Resident)",
            "A resident Deckle Folders holds it. The self-check neither takes it nor waits for it, so it can run beside one."),
        new("microphone probe, settings, tray",
            "This app has none of them: Deckle Folders window reaches nothing outside its own data root."),
    ];

    // OnLaunched cannot await a mode, so the whole run sits in one net: whatever
    // escapes becomes the report's Fatal and the exit code, and the process ends.
    private async Task RunSelfCheckModeAsync(string[] args)
    {
        int flag = Array.IndexOf(args, SelfCheckFlag);
        string? outputFolder = flag + 1 < args.Length ? args[flag + 1] : null;

        // Before AppPaths is touched: its static constructor creates folders in
        // whatever root it resolves, which on a refused run would be the
        // maintainer's.
        SelfCheckGateResult gate = SelfCheckDataRootGate.Evaluate(
            Environment.GetEnvironmentVariable(AppPaths.DataRootEnvVar), DefaultDataRoot.Resolve());

        // A target that names nothing refuses the run here, before a window
        // opens: walking nothing must never read as a pass.
        string? only = SelfCheckTarget.ReadArgument(args);
        string targetReason = "";
        SelfCheckTarget? target = only is null ? null : SelfCheckTarget.Parse(only, Catalog, out targetReason);

        if (!gate.Allowed || string.IsNullOrWhiteSpace(outputFolder) || (only is not null && target is null))
        {
            string reason = !gate.Allowed ? gate.Reason
                : string.IsNullOrWhiteSpace(outputFolder) ? "No output folder: usage is --self-check <output-folder> [--only <target>]."
                : targetReason;
            WriteRefusal(outputFolder, reason);
            TerminateWithoutExitHandlers(Refused);
            return;
        }

        var report = new SelfCheckReport
        {
            Version = ProcessVersion.Display(),
            Build = BuildConfiguration,
            DataRoot = AppPaths.UserDataRoot,
            StartedAt = DateTimeOffset.Now,
            Composed = [.. ComposedBySelfCheck],
            LeftOut = [.. LeftOutOfSelfCheck],
            Only = target?.Name,
            RequiredWindows = Catalog.Windows,
        };

        try
        {
            Directory.CreateDirectory(outputFolder);
            await RunSelfCheckAsync(report, outputFolder, target);
            report.Completed = true;
            report.Current = null;
        }
        catch (Exception ex)
        {
            report.Fatal = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            // The sinks write off the emitting thread: drain before reading.
            DiagnosticsHost.Flush(FlushDeadline);
            try
            {
                report.Errors = SelfCheckLogReader.ReadSevereFromFile(
                    Path.Combine(AppPaths.DiagnosticsDirectory, DiagnosticsFiles.Log));
            }
            catch (Exception ex)
            {
                report.Fatal ??= $"The run's log could not be read: {ex.GetType().Name}: {ex.Message}";
            }
            try { report.Save(outputFolder); }
            catch (Exception ex) { report.Fatal ??= $"The report could not be written: {ex.GetType().Name}"; }
        }

        report.Judge();
        Environment.Exit(report.Outcome == "passed" ? Passed : Failed);
    }

    private async Task RunSelfCheckAsync(SelfCheckReport report, string outputFolder, SelfCheckTarget? target)
    {
        void Step(string name)
        {
            report.Current = name;
            report.Save(outputFolder);
        }

        Step("foundation");
        StartDiagnostics();
        ContributionHost host = EnsureHost();

        // Images go beside the report: the data root is temporary.
        string captures = Path.Combine(outputFolder, "captures");
        InterfaceCaptureRun run = InterfaceCaptureRun.Begin(DateTimeOffset.Now, captures);

        foreach (var walk in host.Walks)
        {
            if (target is not null && target.Window != walk.WindowName) continue;
            Step(walk.WindowName);
            await walk.Run(new FoldersWalkContext(host, report, run, outputFolder, target?.View, Step));
        }

        run.Complete();
    }
}

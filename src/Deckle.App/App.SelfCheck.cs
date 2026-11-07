using System.Runtime.InteropServices;
using Deckle.App.SelfCheck;
using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Vision;
using WinRT.Interop;

namespace Deckle.App;

// ── Self-check: the app opened for real, beside the resident one ──────────────
//
// `Deckle.exe --self-check <output-folder>` is a separate application mode, like
// the install modes: OnLaunched gates it before the resident ownership, and
// nothing of the normal boot runs after it. It exists because the unit suites
// cannot see what only a running app shows (a settings page that overflows the
// stack when it opens, a native library that landed in the wrong folder), and
// the rules forbid an agent to stop or relaunch the maintainer's resident app.
//
// What it does: refuses to run unless DECKLE_DATA_ROOT names a root of its own;
// composes a short, explicit list of pieces (below); probes the microphone once;
// opens the Settings window and walks every page of the rail, then opens the Log
// window and the Playground; photographs each; writes report.json (the contract
// with scripts/self-check.ps1) and exits 0 when all is well, 1 otherwise.
//
// What it composes, in one place. Everything else of the boot is left out, and
// each piece has its reason in LeftOutOfSelfCheck: a piece that acts on the
// machine (a hook, a device, the network, the lights) has no business in a run
// whose job is to look, and a second resident would act twice.
public partial class App
{
    internal const string SelfCheckFlag = "--self-check";

    internal static bool IsSelfCheckMode(string[] args) => Array.IndexOf(args, SelfCheckFlag) >= 0;

    // Exit codes the script reads next to the report.
    private const int SelfCheckPassed = 0;
    private const int SelfCheckFailed = 1;
    private const int SelfCheckRefused = 2;

    private static readonly string[] ComposedBySelfCheck =
    [
        "diagnostics: local sinks (log.jsonl), application log, log window sink",
        "module registry and presence flags (a fresh root has no choice, so every module is present)",
        "settings surface: host hooks and every module's pages and search entries, as the resident app registers them",
        "theme from the settings of the run's own root",
        "windows: Settings, Log, Playground, created by the same lazy paths the tray uses",
        "microphone: one shared open and close through MicrophoneCapture.Probe",
        "interface capture: the walk and the WindowSnapshot of the interface capture",
    ];

    private static readonly SelfCheckLeftOut[] LeftOutOfSelfCheck =
    [
        new("resident ownership (Local\\Deckle.Resident)",
            "The resident app holds it. The self-check neither takes it nor waits for it, so it can run beside the resident app."),
        new("crash supervisor (Program.Main)",
            "The supervisor swallows the child's exit code and relaunches it. The self-check runs unsupervised so a crash exit code reaches the caller."),
        new("transcription engine, speech engine",
            "They need the native runtime and a model, open the audio device and load the GPU. The Dictation pages must render without them: if one cannot, it fails."),
        new("ambient engine",
            "Starting it captures the screen and talks to the Hue bridge."),
        new("trackpad, precision scroll, mouse wheel, input host",
            "Raw input devices and hooks that change how the pointer behaves."),
        new("autocorrect",
            "A global keyboard hook that injects text into other applications."),
        new("paragraph rewrite",
            "Foreground and UI Automation probes of other applications."),
        new("foreground watcher, taskbar cover",
            "A WinEvent hook on every foreground change, and a window drawn over the taskbar."),
        new("orchestration",
            "A WMI subscription on process starts."),
        new("Anytype backend and MCP",
            "Starts a local server and opens the maintainer's space."),
        new("hotkeys, tray icon and menus, message-only host, cursor signal",
            "Global hotkeys and the shell icon belong to the resident app: a second set would answer every press twice."),
        new("HUD, overlays, transcript panel",
            "Topmost windows driven by the engine and the cursor signal, neither composed."),
        new("update checks",
            "They reach the network, and a toast would follow."),
        new("notifications dispatcher and toast channel",
            "A self-check shows no toast."),
        new("telemetry, network status emitter, crash dump report",
            "Datasets and observers nobody asked this run for."),
        new("dictation recovery, speech repair offer, command-line flags (--settings, data root cleanup)",
            "They act on the take or the data of a resident run."),
    ];

    // True when the self-check mode owns the process and OnLaunched must stop.
    private bool TryEnterSelfCheckMode()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!IsSelfCheckMode(args)) return false;

        _ = RunSelfCheckModeAsync(args);
        return true;
    }

    // OnLaunched cannot await a mode, so the whole run sits in one net: whatever
    // escapes becomes the report's Fatal and the exit code, and the process ends.
    private async Task RunSelfCheckModeAsync(string[] args)
    {
        int flag = Array.IndexOf(args, SelfCheckFlag);
        string? outputFolder = flag + 1 < args.Length ? args[flag + 1] : null;

        // Before AppPaths is touched: its static constructor creates folders and
        // moves files in whatever root it resolves, which on a refused run would
        // be the maintainer's.
        SelfCheckGateResult gate = SelfCheckDataRootGate.Evaluate(
            Environment.GetEnvironmentVariable(AppPaths.DataRootEnvVar), DefaultDataRoot.Resolve());
        if (!gate.Allowed || string.IsNullOrWhiteSpace(outputFolder))
        {
            string reason = gate.Allowed ? "No output folder: usage is --self-check <output-folder>." : gate.Reason;
            WriteRefusal(outputFolder, reason);
            TerminateWithoutExitHandlers(SelfCheckRefused);
            return;
        }

        var report = new SelfCheckReport
        {
            Version = CurrentDisplayVersion(),
            Build = BuildConfiguration,
            DataRoot = AppPaths.UserDataRoot,
            StartedAt = DateTimeOffset.Now,
            Composed = [.. ComposedBySelfCheck],
            LeftOut = [.. LeftOutOfSelfCheck],
        };

        int code;
        try
        {
            Directory.CreateDirectory(outputFolder);
            await RunSelfCheckAsync(report, outputFolder);
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
            AppDiagnosticsBootstrap.Flush(ExitFlushDeadline);
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
        code = report.Outcome == "passed" ? SelfCheckPassed : SelfCheckFailed;
        Environment.Exit(code);
    }

    // The explicit list: the steps below are the whole composition of the mode.
    private async Task RunSelfCheckAsync(SelfCheckReport report, string outputFolder)
    {
        void Step(string name)
        {
            report.Current = name;
            report.Save(outputFolder);
        }

        Step("foundation");
        PathRedaction.AddRoot(PathRedaction.DataRootToken, AppPaths.UserDataRoot);
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        DeckleAppSource.Log.AppStarting();
        EmitRunIdentity();
        Settings.SettingsBootstrap.MigrateLegacyToPerModule();
        OperationalLogAdmission.Configure(
            activity => LoggingSettingsService.Instance.Current.Verbosity(activity));
        var context = new StartupContext();
        RegisterModulesAndReadPresence(context);
        AppDiagnosticsBootstrap.InitializeOperationalSinks(AppPaths.DiagnosticsDirectory);
        AppDiagnosticsBootstrap.CompleteBoot();

        Step("settings_surface");
        WireSettingsSurface(context);
        ApplyTheme(Settings.SettingsService.Instance.Current.Appearance.Theme);
        // The Playground opens only when the developer setting allows it. The
        // setting is written in this run's own root, never the maintainer's.
        Settings.SettingsService.Instance.Current.Developer.ShowPlayground = true;
        Settings.SettingsService.Instance.Save();

        Step("microphone");
        report.Microphone = await ProbeMicrophoneForSelfCheckAsync();
        report.Save(outputFolder);

        // Images go beside the report: the data root is temporary.
        string captures = Path.Combine(outputFolder, "captures");
        InterfaceCaptureRun run = InterfaceCaptureRun.Begin(DateTimeOffset.Now, captures);

        Step("settings");
        ShowSettingsWindowLazy();
        if (_settingsWindow is not { } settings)
            throw new InvalidOperationException("The Settings window was not created.");
        nint settingsHwnd = WindowNative.GetWindowHandle(settings);
        await WaitUntilLoadedAsync(settings);

        string currentPage = "";
        string currentLabel = "";
        await settings.WalkPagesAsync(
            async visit =>
            {
                string slug = InterfaceCaptureNames.PageSlug(visit.Tag);
                bool shot = visit.Loaded
                    && await run.CaptureAsync(settingsHwnd, "settings", slug, visit.Label);
                report.Surfaces.Add(new SelfCheckSurface(
                    "settings", slug, visit.Label, visit.Loaded, visit.LoadMs,
                    shot && run.LastFile is { } file ? Path.Combine("captures", file) : null));
                Step($"settings/{slug} done");
            },
            starting: (tag, label) =>
            {
                currentPage = InterfaceCaptureNames.PageSlug(tag);
                currentLabel = label;
                Step($"settings/{currentPage} ({currentLabel})");
            });

        Step("log");
        await OpenAndCaptureAsync(report, run, outputFolder, "log", ShowLogWindowLazy, () => _logWindow);

        Step("playground");
        await OpenAndCaptureAsync(report, run, outputFolder, "playground", ShowPlaygroundLazy, () => _playgroundWindow);

        run.Complete();
        Settings.SettingsService.Instance.Flush();
    }

    private static async Task OpenAndCaptureAsync(
        SelfCheckReport report,
        InterfaceCaptureRun run,
        string outputFolder,
        string name,
        Action open,
        Func<Microsoft.UI.Xaml.Window?> window)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        open();
        Microsoft.UI.Xaml.Window? opened = window();
        bool loaded = opened is not null && await WaitUntilLoadedAsync(opened);
        bool shot = loaded && await run.CaptureAsync(WindowNative.GetWindowHandle(opened!), name);
        report.Surfaces.Add(new SelfCheckSurface(
            name, null, null, loaded, clock.ElapsedMilliseconds,
            shot && run.LastFile is { } file ? Path.Combine("captures", file) : null));
        report.Save(outputFolder);
    }

    // The window's content reached Loaded, then two frames and a short settle,
    // as the page walk waits before a capture.
    private static async Task<bool> WaitUntilLoadedAsync(Microsoft.UI.Xaml.Window window)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (window.Content is Microsoft.UI.Xaml.FrameworkElement { IsLoaded: true })
            {
                await Task.Delay(600);
                return true;
            }
            await Task.Delay(30);
        }
        return false;
    }

    // One shared open and close of the default microphone. Off the UI thread:
    // the probe blocks on the audio device. The native library failing to load
    // is told apart from a device that is absent or busy (SelfCheckMicrophone).
    private static async Task<SelfCheckMicrophone> ProbeMicrophoneForSelfCheckAsync()
    {
        Audio.ProbeResult probe = await Task.Run(() => new Audio.MicrophoneCapture().Probe(""));
        SelfCheckMicrophone outcome = SelfCheckMicrophone.From(
            probe.Ok, probe.Stage, probe.Kind,
            Audio.MicrophoneCapture.ErrorName(probe.ErrorCode),
            Audio.MicrophoneCapture.FormatErrorCode(probe.ErrorCode));

        // The probe logs nothing of its own. A library that did not load is a
        // fault of the build: it goes in the log as the Error a take would
        // have raised, so the report's error list carries it too.
        if (outcome.Outcome is SelfCheckMicrophone.LoadFailed or SelfCheckMicrophone.InitializeFailed)
            Audio.DeckleAudioSource.Log.MicrophoneOpenFailed(
                "self_check", probe.Stage ?? "open", outcome.ErrorCode, outcome.ErrorName, "", "");
        return outcome;
    }

    // A refusal writes its reason beside where the report would have been, so the
    // caller reads it, and ends the process with no handler: ProcessExit would
    // touch AppPaths, which is the root the gate has just protected.
    private static void WriteRefusal(string? outputFolder, string reason)
    {
        if (string.IsNullOrWhiteSpace(outputFolder)) return;
        try
        {
            Directory.CreateDirectory(outputFolder);
            File.WriteAllText(Path.Combine(outputFolder, SelfCheckReport.FileName),
                System.Text.Json.JsonSerializer.Serialize(
                    new { outcome = "refused", reason },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // The exit code still says refused.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    private static void TerminateWithoutExitHandlers(int code)
        => TerminateProcess(GetCurrentProcess(), (uint)code);
}

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
// window and the Playground, then the Mail window and its walk of drawn states;
// photographs each; writes report.json (the contract
// with scripts/self-check.ps1) and exits 0 when all is well, 1 otherwise.
//
// `--only <target>` after the folder keeps the run to one window, or to one
// view of Settings, Mail or the Atelier (SelfCheckTarget): the check of a
// single change, in seconds.
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
        "windows: Settings, Log, Playground, Mail, Atelier, created by the same lazy paths the tray uses",
        "atelier: a sample folder generated under the run's own root, added as the picker adds one",
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
        new("taskbar tray icons, taskbar engines",
            "The tray icons write the user's notify icon settings and keep a registry watcher on that key; the engines inject a DLL into the real explorer and Start menu host. The Taskbar page still opens, and its switches are never touched."),
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

        // A target that names nothing refuses the run here, before a window
        // opens: walking nothing must never read as a pass.
        string? only = SelfCheckTarget.ReadArgument(args);
        string targetReason = "";
        SelfCheckTarget? target = only is null ? null : SelfCheckTarget.Parse(only, out targetReason);

        if (!gate.Allowed || string.IsNullOrWhiteSpace(outputFolder) || (only is not null && target is null))
        {
            string reason = !gate.Allowed ? gate.Reason
                : string.IsNullOrWhiteSpace(outputFolder) ? "No output folder: usage is --self-check <output-folder> [--only <target>]."
                : targetReason;
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
            Only = target?.Name,
        };

        int code;
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
    // With a target (--only), the foundation is composed the same and only the
    // named window is opened; the microphone probe is left out with the rest.
    private async Task RunSelfCheckAsync(SelfCheckReport report, string outputFolder, SelfCheckTarget? target)
    {
        void Step(string name)
        {
            report.Current = name;
            report.Save(outputFolder);
        }

        bool Wants(string window) => target is null || target.Window == window;

        Step("foundation");
        PathRedaction.AddRoot(PathRedaction.DataRootToken, AppPaths.UserDataRoot);
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        DeckleAppSource.Log.AppStarting();
        EmitRunIdentity();
        OperationalLogAdmission.Configure(
            activity => LoggingSettingsService.Instance.Current.Verbosity(activity));
        var context = new StartupContext();
        RegisterModulesAndReadPresence(context);
        AppDiagnosticsBootstrap.InitializeOperationalSinks(AppPaths.DiagnosticsDirectory);
        AppDiagnosticsBootstrap.CompleteBoot();

        Step("settings_surface");
        WireSettingsSurface(context);
        ApplyTheme(Settings.SettingsService.Instance.Current.Appearance.Theme);
        // The Playground opens only while Developer mode is on. The setting is
        // written in this run's own root, never the maintainer's.
        Settings.SettingsService.Instance.Current.Developer.DeveloperMode = true;
        Settings.SettingsService.Instance.Save();

        if (target is null)
        {
            Step("microphone");
            report.Microphone = await ProbeMicrophoneForSelfCheckAsync();
            report.Save(outputFolder);
        }

        // Images go beside the report: the data root is temporary.
        string captures = Path.Combine(outputFolder, "captures");
        InterfaceCaptureRun run = InterfaceCaptureRun.Begin(DateTimeOffset.Now, captures);

        // Windows are shown off every monitor and never activated
        // (OffscreenWindow), and each page is rendered whole by its own XAML
        // (ElementImage), so nothing appears on the person's desktop and no
        // image is cut at a window edge.
        if (Wants("settings"))
        {
            Step("settings");
            Settings.SettingsWindow settings = EnsureSettingsWindow();
            OffscreenWindow.ShowInactive(settings);
            await WaitUntilLoadedAsync(settings);
            // A page target asks for that page alone: the window's own frame
            // and the other pages are not its subject.
            string? onlyPage = target?.View;
            var offered = new List<string>();
            if (onlyPage is null)
                report.Surfaces.Add(await CaptureWindowFrameAsync(run, settings, "settings", "window", null, true, 0));

            string currentPage = "";
            string currentLabel = "";
            await settings.WalkPagesAsync(
                async visit =>
                {
                    string slug = InterfaceCaptureNames.PageSlug(visit.Tag);
                    (int closed, int left) = await CloseTransientPopupsAsync(settings);
                    SelfCheckSurface surface = new("settings", slug, visit.Label, visit.Loaded, visit.LoadMs, null,
                        closed, left, "page-render");
                    if (visit.Loaded && settings.CurrentPage is { } page)
                    {
                        try
                        {
                            ElementImage.Result image = await ElementImage.CapturePageAsync(
                                page, Path.Combine(captures, $"settings-{slug}"));
                            string[] files = [.. image.Files.Select(f => Path.GetRelativePath(outputFolder, f))];
                            surface = surface with
                            {
                                Png = files[0],
                                Parts = files.Length > 1 ? files : null,
                                Width = image.Width,
                                Height = image.Height,
                            };
                        }
                        catch (Exception ex)
                        {
                            surface = surface with { Error = $"{ex.GetType().Name}: {ex.Message}" };
                        }
                    }
                    report.Surfaces.Add(surface);
                    Step($"settings/{slug} done");
                },
                starting: (tag, label) =>
                {
                    currentPage = InterfaceCaptureNames.PageSlug(tag);
                    currentLabel = label;
                    Step($"settings/{currentPage} ({currentLabel})");
                },
                // Every slug the rail offers is noted on the way, so a target
                // that matches none is answered with the list (SelfCheckReport).
                only: onlyPage is null ? null : tag =>
                {
                    string slug = InterfaceCaptureNames.PageSlug(tag);
                    offered.Add(slug);
                    return slug == onlyPage;
                });
            if (onlyPage is not null && !offered.Contains(onlyPage)) report.Views = offered;
        }

        if (Wants("log"))
        {
            Step("log");
            await OpenAndCaptureAsync(report, run, outputFolder, "log", EnsureLogWindow);
        }

        if (Wants("playground"))
        {
            Step("playground");
            await OpenAndCaptureAsync(report, run, outputFolder, "playground", EnsurePlaygroundWindow);
        }

        if (Wants("mail"))
        {
            // The Mail window on its built-in sample: the window once, then each
            // state the maintainer drew, page by page (MailWindow.Capture.cs), with
            // the whole window again where the rail or the title bar changes.
            Step("mail");
            Mail.Hub.MailWindow mail = EnsureMailWindow();
            OffscreenWindow.ShowInactive(mail);
            await WaitUntilLoadedAsync(mail);
            // A view that the walk does not have is answered with the ones it
            // has, and nothing is walked.
            string? onlyView = target?.View;
            if (onlyView is not null && !Mail.Hub.MailWindow.CaptureSlugs.Contains(onlyView))
                report.Views = [.. Mail.Hub.MailWindow.CaptureSlugs];
            else await mail.WalkCapturesAsync(
                async visit =>
                {
                    // A menu, flyout or dialog left open on purpose is the subject
                    // of the step: the window is photographed with it, and the
                    // walk closes it afterwards.
                    if (visit.KeepsPopup)
                    {
                        report.Surfaces.Add(await CaptureWindowFrameAsync(
                            run, mail, "mail", visit.Slug, visit.Label, visit.Loaded, visit.LoadMs));
                        Step($"mail/{visit.Slug} done");
                        return;
                    }
                    (int closed, int left) = await CloseTransientPopupsAsync(mail);
                    SelfCheckSurface surface = new("mail", visit.Slug, visit.Label, visit.Loaded, visit.LoadMs, null,
                        closed, left, "page-render");
                    if (visit.Loaded && mail.CurrentPage is { } page)
                    {
                        try
                        {
                            ElementImage.Result image = await ElementImage.CapturePageAsync(
                                page, Path.Combine(captures, $"mail-{visit.Slug}"));
                            string[] files = [.. image.Files.Select(f => Path.GetRelativePath(outputFolder, f))];
                            surface = surface with
                            {
                                Png = files[0],
                                Parts = files.Length > 1 ? files : null,
                                Width = image.Width,
                                Height = image.Height,
                            };
                        }
                        catch (Exception ex)
                        {
                            surface = surface with { Error = $"{ex.GetType().Name}: {ex.Message}" };
                        }
                    }
                    report.Surfaces.Add(surface);
                    if (visit.WholeWindow)
                        report.Surfaces.Add(await CaptureWindowFrameAsync(
                            run, mail, "mail", $"{visit.Slug}-window", visit.Label, visit.Loaded, visit.LoadMs));
                    Step($"mail/{visit.Slug} done");
                },
                starting: (slug, label) => Step($"mail/{slug} ({label})"),
                only: onlyView);
        }

        if (Wants("atelier"))
        {
            Step("atelier");
            // With a view for target, the walk is played up to it and ends
            // there (SelfCheckWalkGate): that end is not a failure.
            var gate = new SelfCheckWalkGate(target?.View);
            try { await WalkAtelierAsync(report, run, outputFolder, Step, gate); }
            catch (OperationCanceledException) when (gate.Done) { }
            if (target?.View is not null && !gate.Reached) report.Views = gate.Offered;
        }

        run.Complete();
        Settings.SettingsService.Instance.Flush();
    }

    private static async Task OpenAndCaptureAsync(
        SelfCheckReport report,
        InterfaceCaptureRun run,
        string outputFolder,
        string name,
        Func<Microsoft.UI.Xaml.Window> ensure)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Microsoft.UI.Xaml.Window window = ensure();
        OffscreenWindow.ShowInactive(window);
        bool loaded = await WaitUntilLoadedAsync(window);
        (int closed, int left) = loaded ? await CloseTransientPopupsAsync(window) : (0, 0);
        report.Surfaces.Add(await CaptureWindowFrameAsync(
            run, window, name, null, null, loaded, clock.ElapsedMilliseconds, closed, left));
        report.Save(outputFolder);
    }

    // The window as a whole. The pixels DWM composed for it (WindowSnapshot) are
    // the truest, backdrop and caption buttons included; a window nobody can see
    // may deliver none, and then its XAML content is rendered instead, which the
    // technique field of the report says.
    private static async Task<SelfCheckSurface> CaptureWindowFrameAsync(
        InterfaceCaptureRun run,
        Microsoft.UI.Xaml.Window window,
        string name,
        string? page,
        string? label,
        bool loaded,
        long ms,
        int closed = 0,
        int left = 0)
    {
        string technique = "window-capture";
        string? file = null;
        int width = 0, height = 0;
        string? error = null;
        if (loaded)
        {
            nint hwnd = WindowNative.GetWindowHandle(window);
            if (await run.CaptureAsync(hwnd, name, page, label) && run.LastFile is { } captured)
            {
                file = Path.Combine("captures", captured);
            }
            else
            {
                technique = "window-render";
                try
                {
                    string target = Path.Combine(run.Directory, page is null ? $"{name}.png" : $"{name}-{page}.png");
                    ElementImage.Result image = await ElementImage.CaptureWindowContentAsync(window, target);
                    file = Path.Combine("captures", Path.GetFileName(image.Files[0]));
                    width = image.Width;
                    height = image.Height;
                }
                catch (Exception ex)
                {
                    error = $"{ex.GetType().Name}: {ex.Message}";
                }
            }
        }
        return new SelfCheckSurface(name, page, label, loaded, ms, file, closed, left, technique, width, height, null, error);
    }

    // A capture shows the page alone. The walk itself leaves transient UI open:
    // in the compact rail, selecting a child of a group opens the group's
    // flyout, and it stays over the next pages (seen on the Diagnostics image).
    // Every popup open over the window is closed before its image is taken; the
    // count closed is recorded, and one that refuses to close is a failure of
    // that image, so a reviewer never judges a page hidden behind a popup.
    private static async Task<(int Closed, int StillOpen)> CloseTransientPopupsAsync(Microsoft.UI.Xaml.Window window)
    {
        if (window.Content?.XamlRoot is not { } root) return (0, 0);

        var open = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root);
        int closed = 0;
        foreach (Microsoft.UI.Xaml.Controls.Primitives.Popup popup in open)
        {
            popup.IsOpen = false;
            closed++;
        }
        if (closed == 0) return (0, 0);

        // The close reaches the screen on the next frames.
        await Task.Delay(300);
        return (closed, Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count);
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

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
    private sealed class StartupContext
    {
        private readonly System.Diagnostics.Stopwatch? _stopwatch;
        private readonly List<string>? _milestones;

        internal StartupContext()
        {
            TraceEnabled = DeckleAppSource.Log.IsEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Keywords.Lifecycle);
            _stopwatch = TraceEnabled ? System.Diagnostics.Stopwatch.StartNew() : null;
            _milestones = TraceEnabled ? [] : null;
        }

        internal bool TraceEnabled { get; }
        internal bool TranscriptionPresent { get; set; }
        internal bool RewritePresent { get; set; }
        internal bool AutocorrectPresent { get; set; }
        internal bool AmbientPresent { get; set; }
        internal bool TrackpadPresent { get; set; }
        internal bool AnytypePresent { get; set; }

        internal void Milestone(string name)
        {
            if (TraceEnabled)
                _milestones!.Add($"{name} +{_stopwatch!.ElapsedMilliseconds}ms");
        }

        // Every piece of the resident app composes through here, so the
        // journal names what started, what was skipped and why, and what
        // failed. The exception goes on as before: App.xaml.cs's net handles
        // it, and this line is what says which module it came from.
        internal void Compose(string module, Action compose)
        {
            try
            {
                compose();
            }
            catch (Exception ex)
            {
                DeckleAppSource.Log.ModuleFailed(module, ex.GetType().Name, ex.Message);
                throw;
            }
            DeckleAppSource.Log.ModuleStarted(module);
            Milestone(module);
        }

        internal void Skip(string module, string reason)
        {
            DeckleAppSource.Log.ModuleSkipped(module, reason);
            Milestone($"{module}_skipped");
        }

        internal void Complete()
        {
            if (TraceEnabled)
            {
                _stopwatch!.Stop();
                _milestones!.Add($"total {_stopwatch.ElapsedMilliseconds}ms");
                DeckleAppSource.Log.StartupMilestones(string.Join(" | ", _milestones));
            }
            DeckleAppSource.Log.AppReady();
        }
    }

    private void InitializeStartupFoundation(StartupContext context)
    {
        // First, before settings migration: a listener only sees events emitted
        // after it subscribes, so these ungated local sinks must exist for a
        // failure in the very first boot step to land in journal.jsonl.
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        DeckleAppSource.Log.AppStarting();
        EmitRunIdentity();
        context.Milestone("diagnostics-local");

        // Before any module SettingsService is touched: each singleton loads its
        // file in its constructor, and one that ran first would find its module
        // file missing, write defaults and defeat the migration.
        Settings.SettingsBootstrap.MigrateLegacyToPerModule();
        OperationalLogAdmission.Configure(activity => activity switch
        {
            OperationalLogActivity.Ambient =>
                LoggingSettingsService.Instance.Current.LogAmbientCaptureActivity,
            OperationalLogActivity.Transcription =>
                LoggingSettingsService.Instance.Current.LogTranscriptionActivity,
            OperationalLogActivity.Autocorrect =>
                LoggingSettingsService.Instance.Current.LogAutocorrectActivity,
            OperationalLogActivity.Input =>
                LoggingSettingsService.Instance.Current.LogInputActivity,
            OperationalLogActivity.Windowing =>
                LoggingSettingsService.Instance.Current.LogWindowingActivity,
            _ => false,
        });
        EmitLoggingPolicy();
        context.Milestone("settings-bootstrap");

        // Before any module is composed or registered: every gate below reads
        // these flags.
        AppModules.RegisterAll();
        context.TranscriptionPresent = ModulePresence.IsPresent(ModuleIds.Transcription);
        context.RewritePresent = ModulePresence.IsPresent(ModuleIds.Rewrite);
        context.AutocorrectPresent = ModulePresence.IsPresent(ModuleIds.Autocorrect);
        context.AmbientPresent = ModulePresence.IsPresent(ModuleIds.Ambient);
        context.TrackpadPresent = ModulePresence.IsPresent(ModuleIds.Trackpad);
        context.AnytypePresent = ModulePresence.IsPresent(ModuleIds.Anytype);
        context.Milestone("modules");

        // Injected so Deckle.Core reads the storage directory without depending
        // on the telemetry module.
        Deckle.Core.CorpusPaths.ConfigureStorageDirectoryOverride(() =>
        {
            string s = TelemetrySettingsService.Instance.Current.StorageDirectory;
            return string.IsNullOrWhiteSpace(s) ? null : s;
        });

        AppDiagnosticsBootstrap.InitializeOperationalSinks(AppPaths.DiagnosticsDirectory);
        AppDiagnosticsBootstrap.InitializeTelemetry(AppPaths.TelemetryDirectory);
        AppDiagnosticsBootstrap.CompleteBoot();
        context.Milestone("diagnostics");

        Deckle.Diagnostics.Telemetry.TelemetryListenerBootstrap.ConfigureGates(name => name switch
        {
            "LatencyEnabled"        => TelemetrySettingsService.Instance.Current.LatencyEnabled,
            "MicrophoneTelemetry"   => TelemetrySettingsService.Instance.Current.MicrophoneTelemetry,
            "CorpusEnabled"         => TelemetrySettingsService.Instance.Current.CorpusEnabled,
            "AutocorrectDecisions"  => TelemetrySettingsService.Instance.Current.AutocorrectDecisions,
            "AutocorrectText"       => TelemetrySettingsService.Instance.Current.AutocorrectText,
            _                       => false,
        });

        NetworkStatusEmitter.Start();

        ReportCrashDumpCapture();

        // Checked here so the path payloads are not even read when Verbose is
        // not admitted.
        if (DeckleAppSource.Log.IsEnabled(
                System.Diagnostics.Tracing.EventLevel.Verbose,
                (System.Diagnostics.Tracing.EventKeywords)Keywords.Lifecycle))
        {
            DeckleAppSource.Log.PathsDetail(
                AppPaths.UserDataRoot,
                AppPaths.SettingsFilePath,
                AppPaths.TelemetryDirectory,
                AppPaths.ModelsDirectory,
                AppPaths.NativeDirectory);
        }

        // After the diagnostics listeners, so the dispatcher's audit events route
        // from the first emission, and before any surface that could raise a
        // notification. A duplicate descriptor id fails the boot.
        var toastChannel = new Deckle.Notifications.ToastChannel();
        var dispatcher = Deckle.Notifications.NotificationDispatcher.Initialize(toastChannel);
        dispatcher.Catalog.Register(PlaygroundNotifications.All);
        // The updater is shell, not a presence-gated module, so it always registers.
        dispatcher.Catalog.Register(Deckle.Setup.SetupNotifications.All);
        if (context.AutocorrectPresent)
            dispatcher.Catalog.Register(AutocorrectNotifications.All);
        context.Milestone("notifications");
    }

    private const string BuildConfiguration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    // Right after AppStarting, while only the local sinks exist: the boot
    // replay carries it to the later ones, and the journal has it even if the
    // next boot step is the one that fails.
    private static void EmitRunIdentity()
    {
        string[] cliArgs = Environment.GetCommandLineArgs();
        DeckleAppSource.Log.RunIdentity(
            version:       CurrentDisplayVersion(),
            stage:         CurrentStage(),
            build:         BuildConfiguration,
            session:       DeckleEventSource.SessionId,
            pid:           Environment.ProcessId,
            os:            $"{Environment.OSVersion.VersionString} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}",
            supervised:    CrashSupervisor.Supervised,
            follows_crash: CrashSupervisor.FollowsCrash is not null,
            args:          string.Join(' ', cliArgs.Skip(1)));
    }

    // Once the settings are readable: the line says what the journals of this
    // run can contain, and a flag flipped later is logged by the settings page.
    private static void EmitLoggingPolicy()
    {
        var logging = LoggingSettingsService.Instance.Current;
        DeckleAppSource.Log.LoggingPolicy(
            ambient:         logging.LogAmbientCaptureActivity,
            transcription:   logging.LogTranscriptionActivity,
            autocorrect:     logging.LogAutocorrectActivity,
            input:           logging.LogInputActivity,
            windowing:       logging.LogWindowingActivity,
            app_log_to_disk: logging.ApplicationLogToDisk);
    }
}


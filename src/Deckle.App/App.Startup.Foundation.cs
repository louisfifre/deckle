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
        // Always-on local diagnostic sinks (setup.jsonl + errors.jsonl) come
        // FIRST — before settings migration — so an Error in the very first boot
        // step still lands in errors.jsonl. An EventListener only captures events
        // emitted after it subscribes; these sinks read no settings and are
        // ungated, so registering them here is what makes the local trace cover
        // the riskiest, un-opted-in moment. Optional operational and dataset
        // sinks are wired later, after migration.
        AppDiagnosticsBootstrap.InitializeLocalSinks(AppPaths.DiagnosticsDirectory);
        DeckleAppSource.Log.AppStarting();
        context.Milestone("diagnostics-local");

        // Per-module persistence migration runs FIRST — before any module
        // SettingsService.Instance is touched. Detects the legacy combined
        // settings.json and dispatches each module section to its own
        // modules/<id>/settings.json file. Idempotent across launches: on
        // subsequent boots the legacy file no longer carries module
        // sections, so the bootstrap is a no-op.
        //
        // Why first? Because module SettingsService singletons load their
        // file in their constructor — if a module service initialized before
        // the dispatch ran, it would see a missing module file and write
        // defaults, defeating the migration. Telemetry gate wiring below
        // touches TelemetrySettingsService, so we must dispatch before that.
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
        context.Milestone("settings-bootstrap");

        // Presence catalogue + the user's module choice, before any module is
        // composed or registered: every gate below reads these flags. Presence
        // (chosen at install, via the wizard's module selector) sits above the
        // per-module Enabled toggles — an absent module's engine is not
        // composed and its settings pages never register, where a disabled one
        // is merely stopped. No recorded choice means everything is present.
        AppModules.RegisterAll();
        context.TranscriptionPresent = ModulePresence.IsPresent(ModuleIds.Transcription);
        context.RewritePresent = ModulePresence.IsPresent(ModuleIds.Rewrite);
        context.AutocorrectPresent = ModulePresence.IsPresent(ModuleIds.Autocorrect);
        context.AmbientPresent = ModulePresence.IsPresent(ModuleIds.Ambient);
        context.TrackpadPresent = ModulePresence.IsPresent(ModuleIds.Trackpad);
        context.AnytypePresent = ModulePresence.IsPresent(ModuleIds.Anytype);
        context.Milestone("modules");

        // Wiring for `Deckle.Core.CorpusPaths` (relocated in sub-wave 6a):
        // the storage paths helper needed a getter for the user
        // StorageDirectory without depending on an observability module. The
        // dependency is inverted through injection: App wires the getter to the
        // new TelemetrySettingsService.
        Deckle.Core.CorpusPaths.ConfigureStorageDirectoryOverride(() =>
        {
            string s = TelemetrySettingsService.Instance.Current.StorageDirectory;
            return string.IsNullOrWhiteSpace(s) ? null : s;
        });

        // Operational journals and purpose-specific datasets are wired as two
        // authorities. app.jsonl and the LogWindow consume operational events;
        // telemetry sinks consume only explicitly tagged dataset events.
        AppDiagnosticsBootstrap.InitializeOperationalSinks(AppPaths.DiagnosticsDirectory);
        AppDiagnosticsBootstrap.InitializeTelemetry(AppPaths.TelemetryDirectory);
        context.Milestone("diagnostics");

        // Wire user gates on the telemetry sinks side
        // (Deckle.Diagnostics.Telemetry). Direct read from the canonical
        // TelemetrySettingsService.
        Deckle.Diagnostics.Telemetry.TelemetryListenerBootstrap.ConfigureGates(name => name switch
        {
            "LatencyEnabled"        => TelemetrySettingsService.Instance.Current.LatencyEnabled,
            "MicrophoneTelemetry"   => TelemetrySettingsService.Instance.Current.MicrophoneTelemetry,
            "CorpusEnabled"         => TelemetrySettingsService.Instance.Current.CorpusEnabled,
            "AutocorrectDecisions"  => TelemetrySettingsService.Instance.Current.AutocorrectDecisions,
            "AutocorrectText"       => TelemetrySettingsService.Instance.Current.AutocorrectText,
            _                       => false,
        });

        // app.jsonl and LogWindow receive the same admitted operational stream.
        // Their recording and display filters remain independent projections.

        // Cross-cutting Network sub-provider: capture machine network state
        // transitions to correlate business HTTP failures (Hue REST, Ollama)
        // with an OS-level outage or switch. Single idempotent emitter; an
        // initial event is emitted at `Start()` to capture boot state, then on
        // every `NetworkInformation.NetworkStatusChanged` broadcast.
        NetworkStatusEmitter.Start();

        // Resolved paths logged once at boot — useful for support: tells us
        // where the app is looking for settings, models, native DLLs, and
        // telemetry. Touching any AppPaths member triggers the static ctor
        // that resolves <UserDataRoot> and creates the writable directories.
        //
        // This is technical boot context, not a workflow milestone. Refuse it
        // before even touching the path payloads when Verbose is not admitted.
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

        // Notification dispatcher — composition root for user messages. Wired
        // here, right after the diagnostics listeners are live (above), so the
        // audit events the dispatcher emits route from the very first emission;
        // and before any surface that could raise a notification exists (the
        // first-run wizard, the engine, the windows below). The toast channel
        // is the only channel today (the autocorrect enrollment prompt needs
        // native Windows 11 interactive toasts). Each module's descriptors are
        // registered into the central catalogue at this point — Playground's
        // manual test surface among them; duplicate ids fail the boot fast.
        var toastChannel = new Deckle.Notifications.ToastChannel();
        var dispatcher = Deckle.Notifications.NotificationDispatcher.Initialize(toastChannel);
        dispatcher.Catalog.Register(PlaygroundNotifications.All);
        // Setup's update-available prompt — the wizard/updater is shell, not a
        // presence-gated module, so it always registers.
        dispatcher.Catalog.Register(Deckle.Setup.SetupNotifications.All);
        // A module's notification descriptors follow its presence: an absent
        // module has no surface that could raise them.
        if (context.AutocorrectPresent)
            dispatcher.Catalog.Register(AutocorrectNotifications.All);
        context.Milestone("notifications");
    }
}


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
    private void WireStartupSettings(StartupContext context)
    {
        // Deckle.Hud cannot see Settings. The provider is read at every vsync, so
        // a live edit applies at once.
        Deckle.Hud.HudChrono.MaxRecordingDurationSecondsProvider =
            () => Audio.CaptureSettingsService.Instance.Current.MaxRecordingDurationSeconds;

        // Must be wired before any Settings page is created.
        Settings.SettingsHost.ApplyTheme       = ApplyTheme;
        Settings.SettingsHost.RestartApp       = RestartApp;
        Settings.SettingsHost.GetSettingsWindow = () => _settingsWindow;
        // A module page such as Autocorrect cannot see Deckle.Settings, so the
        // drill-in hook lives on the Catalog floor between them.
        Catalog.SettingsNavigation.GoToPage = tag => _settingsWindow?.SelectPage(tag);
        // FolderPickerCard needs the Settings window, so the Catalog composer builds
        // it through this factory. DefaultPath comes from a deferred AppPaths lookup:
        // it is resolved here at compose time, never captured earlier.
        Catalog.SettingsComposer.PathControlFactory = args =>
            args.Mode == Catalog.FolderPickerMode.Editable
                ? new Settings.FolderPickerEditableCard
                {
                    DefaultPath = args.DefaultPath?.Invoke() ?? string.Empty,
                }
                : new Settings.FolderPickerCard
                {
                    Mode = args.Mode,
                    DefaultPath = args.DefaultPath?.Invoke() ?? string.Empty,
                };
        // Before any module settings page is created: its manifest reads the
        // consent registry through confirmOnEnable.
        Settings.TelemetryConsentWiring.Wire();
        Settings.SettingsHost.OpenSetupWizard  = async () =>
        {
            var setup = new Deckle.Setup.SetupWindow();
            setup.Body.Navigate(typeof(Deckle.Setup.ModulesPage), setup);
            setup.Activate();

            // Engines are composed only at startup, so a successful run restarts
            // to recompose. Without transcription, the Dictation page is gone.
            bool ok = await setup.Completion;
            if (ok)
                RestartApp(ModulePresence.IsPresent(ModuleIds.Transcription)
                    ? "Deckle.Transcription.WhisperPage, Deckle.Transcription"
                    : null);
        };

        // Readiness probe for the Dictation settings page's "set up" CTA. That
        // page lives in Deckle.Transcription, which cannot see the Whisper child
        // module, so the App — which composes both — answers here.
        Settings.SettingsHost.IsSpeechProvisioned =
            () => NativeRuntime.IsInstalled() && SpeechModels.IsAnyModelInstalled();

        WireUpdater();

        Settings.SettingsHost.RelocateDataRoot = StartDataRelocation;

        // Read at call time: the actions are built later, in the shell phase.
        Settings.SettingsHost.IsHotkeyActionAvailable =
            id => _hotkeyActions.Any(action => action.ToggleId == id);

        // Both registrations travel together: a nav item without its search
        // entries, or the reverse, would read as a half-installed module. Orders
        // leave gaps so a later module can land between two existing ones.
        void RegisterSettingsModule(
            Catalog.SettingsModuleDescriptor page,
            System.Collections.Generic.IReadOnlyList<Catalog.SettingSearchEntry> entries)
        {
            Settings.SettingsModuleRegistry.Register(page);
            Settings.SettingsSearchIndex.Register(page, entries);
        }

        if (context.TranscriptionPresent)
        {
            RegisterSettingsModule(Audio.RecordingSettingsModule.Describe(order: 50), Audio.SettingsSearch.Entries);
            RegisterSettingsModule(Transcription.WhisperSettingsModule.Describe(order: 100), Transcription.SettingsSearch.Entries);
        }
        if (context.RewritePresent)
            RegisterSettingsModule(Llm.Rewrite.LlmSettingsModule.Describe(order: 200), Llm.Rewrite.SettingsSearch.Entries);
        if (context.AutocorrectPresent)
        {
            // One family, three rail entries: the parent page, then its two children
            // (nested through their descriptors' ParentId). Each registers on its own
            // because the search index resolves page coordinates per registration —
            // a child folded into its parent's call would index its cards against the
            // parent's tag and send every hit to the wrong page.
            RegisterSettingsModule(
                Autocorrect.AutocorrectSettingsModule.Describe(order: 300),
                Autocorrect.SettingsSearch.Entries);
            RegisterSettingsModule(
                Autocorrect.AutocorrectSettingsModule.DescribeLexicalDomains(order: 310),
                Autocorrect.SettingsSearch.LexicalDomainsEntries);
            RegisterSettingsModule(
                Autocorrect.AutocorrectSettingsModule.DescribeAppsEnrolled(order: 320),
                Autocorrect.SettingsSearch.AppsEnrolledEntries);
        }
        if (context.AmbientPresent)
            RegisterSettingsModule(Lighting.Ambient.AmbientSettingsModule.Describe(order: 400), Lighting.Ambient.SettingsSearch.Entries);
        if (context.TrackpadPresent)
            RegisterSettingsModule(Input.Trackpad.TrackpadSettingsModule.Describe(order: 500), Input.Trackpad.SettingsSearch.Entries);
        RegisterSettingsModule(Input.PrecisionScroll.PrecisionScrollSettingsModule.Describe(order: 550), Input.PrecisionScroll.SettingsSearch.Entries);
        // Apps and games is not a presence module: nothing to provision, so it
        // is always registered, like precision scrolling.
        RegisterSettingsModule(Orchestration.OrchestrationSettingsModule.Describe(order: 560), Orchestration.SettingsSearch.Entries);
        // Taskbar cover is always composed, so its page is always registered.
        RegisterSettingsModule(Shell.TaskbarCover.TaskbarCoverSettingsModule.Describe(order: 580), Shell.TaskbarCover.SettingsSearch.Entries);
        // Diagnostics is shell-level observability, not a presence module.
        RegisterSettingsModule(Diagnostics.Logging.DiagnosticsSettingsModule.Describe(order: 600), Diagnostics.Logging.SettingsSearch.Entries);

        // General is a static nav anchor with no descriptor: these values must
        // match its NavigationViewItem in SettingsWindow.xaml.
        Settings.SettingsSearchIndex.RegisterPage(
            "Deckle.Settings.GeneralPage",
            "Deckle.Settings",
            Deckle.Catalog.Glyphs.Home,
            Deckle.Catalog.Loc.GetFrom("Deckle.Settings", "SettingsNavGeneral/Content"),
            Settings.SettingsSearch.Entries);
    }
}


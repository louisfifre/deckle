using Deckle.Core;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;
using Deckle.Hud;
using Deckle.Lighting.Ambient;
using Deckle.Modules;
using Deckle.Setup;
using Deckle.Shell;
using Deckle.Shell.TaskbarCover;
using Deckle.Shell.TrayMenu;
using Deckle.Transcription;
using Deckle.Localization;

namespace Deckle.App;

public partial class App
{
    private void WireStartupSettings(StartupContext context)
    {
        WireSettingsSurface(context);
        StartUpdateChecks();
        OfferOwnAnimations();
    }

    // Everything the Settings window needs to build its pages: the host hooks
    // and the module registrations. Nothing here acts on the machine, so the
    // self-check mode (App.SelfCheck.cs) composes it as the resident app does,
    // and the pages it walks are the pages a person gets.
    private void WireSettingsSurface(StartupContext context)
    {
        // Deckle.Hud does not read settings, so the App hands it the recording cap
        // as a provider. The HUD calls it at every vsync, so a live edit applies.
        Deckle.Hud.HudChrono.MaxRecordingDurationSecondsProvider =
            () => Audio.CaptureSettingsService.Instance.Current.MaxRecordingDurationSeconds;

        // SettingsHost holds the App-side hooks the Settings UI calls back into.
        // Must be wired before any Settings page is created.
        Settings.SettingsHost.ApplyTheme       = ApplyTheme;
        Settings.SettingsHost.RestartApp       = RestartApp;
        Settings.SettingsHost.GetSettingsWindow = () => _settingsWindow;
        Catalog.SettingsNavigation.GoToPage = tag => _settingsWindow?.SelectPage(tag);
        Catalog.SettingsNavigation.OpenSubpage = (page, parameter) => _settingsWindow?.OpenSubpage(page, parameter);
        // The Location line of a folder setting lives in Deckle.Settings (it
        // needs the Settings window and its ETW source), so the Catalog composer
        // builds it through this factory. DefaultPath is invoked here, at
        // compose time, never captured earlier: the fallback shown for an empty
        // value must be current.
        Catalog.SettingsComposer.PathControlFactory = args =>
            new Settings.FolderLocationLine(args.Mode, args.DefaultPath?.Invoke() ?? string.Empty);
        // Fills the shell's consent dialogs into Catalog.TelemetryConsent. Must run
        // before any module settings page is created: its manifest reads the
        // registry through confirmOnEnable.
        Settings.TelemetryConsentWiring.Wire();
        Settings.SettingsHost.OpenSetupWizard  = async () =>
        {
            var setup = new Deckle.Setup.SetupWindow();
            setup.Body.Navigate(typeof(Deckle.Setup.ModulesPage), setup);
            setup.Activate();

            // Engines are composed at startup only for the modules chosen and
            // provisioned, so a successful wizard run needs a restart. Land on the
            // Dictation page only when transcription is still installed: otherwise
            // that page does not exist.
            bool ok = await setup.Completion;
            if (ok)
                RestartApp(ModulePresence.IsPresent(ModuleIds.Transcription)
                    ? Transcription.Views.TranscriptionSettingsModule.PageTag
                    : null);
        };

        // The Dictation page lives in Deckle.Transcription.Views, which cannot see
        // the Parakeet child module, so the App, which composes both, answers
        // here: whether the model folder holds every file the engine needs.
        Settings.SettingsHost.IsSpeechProvisioned = ParakeetModelPresent;

        // Same reason for the state of the model folder: the layout lives in the
        // Parakeet child module, which only the App references.
        Settings.SettingsHost.InspectParakeetModelFolder = configured =>
        {
            string modelsDirectory = TranscriptionSettingsService.Instance.ResolveModelsDirectory();
            var files = Deckle.Transcription.Parakeet.ParakeetModelFiles.Resolve(configured, modelsDirectory);
            return new Settings.ParakeetModelFolderState(
                files.Directory,
                Deckle.Transcription.Parakeet.ParakeetModelCatalog.IsPresent(
                    configured, modelsDirectory, TranscriptionSettingsService.Instance.Current.Engine.ParakeetEncoder),
                files.HasFp16Encoder);
        };

        WireUpdaterHooks();

        // The live app cannot copy its own data root, so it restarts into
        // --relocate-data.
        Settings.SettingsHost.RelocateDataRoot = StartDataRelocation;

        // A module that is absent has no hotkey card. Read at call time: the
        // hotkey actions are built later, when the shell starts.
        Settings.SettingsHost.IsHotkeyActionAvailable =
            id => _hotkeyActions.Any(action => action.ToggleId == id);

        // The rewrite profiles' own shortcuts, as registered: the Shortcuts page
        // lists them and checks its chords against them.
        Settings.SettingsHost.ProfileHotkeys = () => _profileHotkeyActions
            .Select(action => action.Chord() is { } chord
                ? new Settings.ProfileHotkey(action.Label, chord)
                : null)
            .OfType<Settings.ProfileHotkey>()
            .ToList();

        // Developer mode's "Capture the interface" (App.InterfaceCapture.cs).
        Settings.SettingsHost.CaptureInterface = InterfaceCapture.FromSettingsAsync;

        // Each module page declares its own nav identity in its own assembly; the
        // composition root supplies only the Order, which leaves gaps so a later
        // module can land between two. A page registers only when the presence
        // module that owns it is installed, and its rail item and search entries
        // always register together: one without the other reads as a
        // half-installed module.
        void RegisterSettingsModule(
            Catalog.SettingsModuleDescriptor page,
            System.Collections.Generic.IReadOnlyList<Catalog.SettingSearchEntry> entries)
        {
            Settings.SettingsModuleRegistry.Register(page);
            Settings.SettingsSearchIndex.Register(page, entries);
        }

        // Diagnostics is the one editor of what Deckle records about itself. A
        // module hands it its dataset opt-ins through the registry, beside its own
        // page and under the same presence test, and the search lists those cards
        // under the Diagnostics page with the module's assembly, where their words
        // live. Described once here because every contribution needs its page
        // coordinates before the page itself registers, further down.
        var diagnostics = Diagnostics.Logging.Views.DiagnosticsSettingsModule.Describe(order: 600);

        void ContributeDiagnostics(
            Catalog.DiagnosticsContribution contribution,
            string owningAssembly,
            System.Collections.Generic.IReadOnlyList<Catalog.SettingSearchEntry> entries)
        {
            Catalog.DiagnosticsRegistry.Register(contribution);
            Settings.SettingsSearchIndex.RegisterPage(
                diagnostics.PageTag,
                owningAssembly,
                diagnostics.Glyph,
                Deckle.Localization.Loc.GetFrom(diagnostics.OwningAssembly, diagnostics.LabelKey),
                entries);
        }

        if (context.TranscriptionPresent)
        {
            // The microphone level, a developer capture, is the Diagnostics
            // page's own card under Detailed logs, no longer a contribution.
            RegisterSettingsModule(Audio.Views.RecordingSettingsModule.Describe(order: 50), Audio.Views.SettingsSearch.Entries);
            // One module, three rail entries: the module's page (General under the
            // node) then Lexicon and Streaming nested under it, each registered
            // on its own so the search index resolves page coordinates per page.
            RegisterSettingsModule(Transcription.Views.TranscriptionSettingsModule.Describe(order: 100), Transcription.Views.SettingsSearch.Entries);
            RegisterSettingsModule(Transcription.Views.TranscriptionSettingsModule.DescribeLexicon(order: 105), Transcription.Views.SettingsSearch.LexiconEntries);
            RegisterSettingsModule(Transcription.Views.TranscriptionSettingsModule.DescribeStreaming(order: 110), Transcription.Views.SettingsSearch.StreamingEntries);
            ContributeDiagnostics(
                Transcription.Views.TranscriptionTelemetryViewModel.Contribution(order: 100),
                "Deckle.Transcription.Views",
                Transcription.Views.SettingsSearch.TelemetryEntries);
        }

        if (context.RewritePresent)
        {
            // The dictation steps stream only on a streaming transcription;
            // without the transcription module, nothing is dictated anyway.
            bool transcriptionPresent = context.TranscriptionPresent;
            Llm.Rewrite.Views.LlmSettingsModule.TranscriptionStreams = () =>
                transcriptionPresent
                && TranscriptionSettingsService.Instance.Current.Streaming.Strategy == PipelineStrategyKind.Streaming;
            RegisterSettingsModule(Llm.Rewrite.Views.LlmSettingsModule.Describe(order: 200), Llm.Rewrite.Views.SettingsSearch.Entries);
        }
        if (context.AutocorrectPresent)
        {
            // Each page of the family registers on its own: the search index
            // resolves page coordinates per registration, so a child folded into
            // its parent's call would send every hit to the parent's page.
            Autocorrect.Views.AutocorrectSettingsModule.TranscriptionLexiconPresent = context.TranscriptionPresent;
            // Paragraph correction runs in the Rewrite module, which the page does
            // not reference: it is told whether that module is there, what the
            // bar needs to say, and where "Set up Ollama" leads. The monitor is
            // the one answer the host acts on.
            Autocorrect.Views.AutocorrectSettingsModule.ParagraphCorrectionPresent = context.RewritePresent;
            if (context.RewritePresent)
            {
                Autocorrect.Views.AutocorrectSettingsModule.ParagraphCorrectionNeedsSetup = () =>
                    Llm.Rewrite.ParagraphRewriteNotices.NeedsSetup(
                        Llm.Rewrite.ParagraphRewriteAvailabilityMonitor.Instance.Current);
                Autocorrect.Views.AutocorrectSettingsModule.ParagraphCorrectionModel = Llm.Rewrite.ParagraphRewrite.Model;
                Autocorrect.Views.AutocorrectSettingsModule.ParagraphCorrectionSetupPageTag =
                    Llm.Rewrite.Views.LlmSettingsModule.PageTag;
                Llm.Rewrite.ParagraphRewriteAvailabilityMonitor.Instance.Changed +=
                    Autocorrect.Views.AutocorrectSettingsModule.RaiseParagraphCorrectionSetupChanged;
            }
            RegisterSettingsModule(
                Autocorrect.Views.AutocorrectSettingsModule.Describe(order: 300),
                Autocorrect.Views.SettingsSearch.Entries);
            RegisterSettingsModule(
                Autocorrect.Views.AutocorrectSettingsModule.DescribeLexicalDomains(order: 310),
                Autocorrect.Views.SettingsSearch.LexicalDomainsEntries);
            RegisterSettingsModule(
                Autocorrect.Views.AutocorrectSettingsModule.DescribeAppsEnrolled(order: 320),
                Autocorrect.Views.SettingsSearch.AppsEnrolledEntries);
        }
        if (context.AmbientPresent)
            RegisterSettingsModule(Lighting.Ambient.Views.AmbientSettingsModule.Describe(order: 400), Lighting.Ambient.Views.SettingsSearch.Entries);
        if (context.TrackpadPresent)
            RegisterSettingsModule(Input.Trackpad.TrackpadSettingsModule.Describe(order: 500), Input.Trackpad.SettingsSearch.Entries);
        if (context.PrecisionScrollPresent)
            RegisterSettingsModule(Input.PrecisionScroll.PrecisionScrollSettingsModule.Describe(order: 550), Input.PrecisionScroll.SettingsSearch.Entries);
        if (context.OrchestrationPresent)
            RegisterSettingsModule(Orchestration.OrchestrationSettingsModule.Describe(order: 560), Orchestration.SettingsSearch.Entries);
        // Taskbar and its cover are always composed, so their pages are always
        // registered. The cover nests under the Taskbar node on its own
        // ParentId, so it registers after its parent and sorts just below it.
        RegisterSettingsModule(Shell.Taskbar.TaskbarSettingsModule.Describe(order: 580), Shell.Taskbar.SettingsSearch.Entries);
        RegisterSettingsModule(Shell.TaskbarCover.TaskbarCoverSettingsModule.Describe(order: 581), Shell.TaskbarCover.SettingsSearch.Entries);
        // Diagnostics is shell-level observability, not a presence module —
        // always registered.
        RegisterSettingsModule(diagnostics, Diagnostics.Logging.Views.SettingsSearch.Entries);

        // General and Shortcuts are static nav anchors of the shell, not registry
        // modules, so their cards register with the values their NavigationViewItems
        // carry in SettingsWindow.xaml: tag, glyph and x:Uid must stay in step with
        // them.
        Settings.SettingsSearchIndex.RegisterPage(
            "Deckle.Settings.GeneralPage",
            "Deckle.Settings",
            Deckle.Catalog.Glyphs.Settings,
            Deckle.Localization.Loc.GetFrom("Deckle.Settings", "SettingsNavGeneral/Content"),
            Settings.SettingsSearch.Entries);
        Settings.SettingsSearchIndex.RegisterPage(
            "Deckle.Settings.HotkeysPage",
            "Deckle.Settings",
            Deckle.Catalog.Glyphs.Shortcut,
            Deckle.Localization.Loc.GetFrom("Deckle.Settings", "SettingsNavHotkeys/Content"),
            Settings.SettingsSearch.HotkeyEntries);
    }
}


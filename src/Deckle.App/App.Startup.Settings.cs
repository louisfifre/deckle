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
        // Wire the recording cap into the Hud lib. Deckle.Hud is
        // a Settings-agnostic module ; the App is the one that reads
        // Settings on every vsync to honour live edits to MaxRecordingDurationSeconds
        // (Capture page slider). Provider is invoked from UpdateClock at vsync.
        Deckle.Hud.HudChrono.MaxRecordingDurationSecondsProvider =
            () => Audio.CaptureSettingsService.Instance.Current.MaxRecordingDurationSeconds;

        // SettingsHost — App-side hooks the Deckle.Settings UI surface
        // calls back into to drive theme broadcast, level-window
        // propagation, restart, and the parent-window accessor for
        // dialogs. Must be wired before any Settings page is created.
        // Pattern aligned on HudChrono.MaxRecordingDurationSecondsProvider
        // above: lib exposes static delegates, App owns the contract.
        Settings.SettingsHost.ApplyTheme       = ApplyTheme;
        Settings.SettingsHost.RestartApp       = RestartApp;
        Settings.SettingsHost.GetSettingsWindow = () => _settingsWindow;
        // Page-to-page drill-in for the settings surface: a module page hands a
        // destination PageTag, the shell selects the matching rail item (children
        // included) and the Frame follows. Same lib-exposes-a-delegate / App-owns-the
        // -wiring shape as the PathControlFactory below — Deckle.Autocorrect cannot
        // see Deckle.Settings, so the hook lives on the Catalog floor between them.
        Catalog.SettingsNavigation.GoToPage = tag => _settingsWindow?.SelectPage(tag);
        // The Path-kind picker control is module-owned (FolderPickerCard needs the
        // Settings window + ETW source), so the floor composer builds it through
        // this factory — same lib-exposes-delegate / App-owns-contract pattern.
        // Dispatch on Mode: Editable wants the typeable variant (a TextBox the user
        // can paste a transplanted path into), every other mode the read-only card.
        // Both implement IPathControl and take Mode + DefaultPath the same way, so
        // the only difference is which type is newed up. DefaultPath is resolved
        // once here (the deferred AppPaths lookup), never captured earlier — the
        // fallback shown when the value is empty must be computed at compose time.
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
        // Fill the Catalog.TelemetryConsent registry with the shell's consent
        // dialogs so module settings pages can gate their telemetry opt-ins behind
        // the right consent — same lib-exposes-slots / App-owns-wiring pattern as
        // the PathControlFactory above. Must run before any module settings page is
        // created (its manifest reads the registry through confirmOnEnable).
        Settings.TelemetryConsentWiring.Wire();
        Settings.SettingsHost.OpenSetupWizard  = async () =>
        {
            // Wizard XAML lives in the standalone Deckle.Setup module
            // (extracted out of Deckle.App/Shell/Setup/ for J3). Detached
            // from the Settings window — Settings stays open behind it.
            var setup = new Deckle.Setup.SetupWindow();
            setup.Body.Navigate(typeof(Deckle.Setup.ModulesPage), setup);
            setup.Activate();

            // Provisioning AND presence are decoupled from boot: engines are
            // composed at startup only for the modules chosen and provisioned.
            // A successful wizard run (module change, first setup, model swap)
            // therefore needs a restart to (re)compose. Land on the Dictation
            // page when transcription is still part of the install, on the
            // Settings default otherwise — its page no longer exists.
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

        // In-app updater: the General page's version-row hooks, plus the silent
        // background check (installed launches only, opt-out in Settings) whose
        // toast offers the explicit update flow. Lives in App.Update.cs.
        WireUpdater();

        // Data-root move: Settings hands the validated target here; the live
        // app cannot copy its own root, so it restarts into --relocate-data.
        // Lives in App.Relocate.cs.
        Settings.SettingsHost.RelocateDataRoot = StartDataRelocation;

        // Settings module nav registry + cross-page search index — each module-owned
        // settings page declares its own nav identity (page tag + module PRI + icon) in
        // its own assembly, via its <Module>SettingsModule.Describe(order). The
        // composition root supplies ONLY the Order here, so the shell builds their
        // NavigationView items from the registry instead of hardcoding them in
        // SettingsWindow.xaml. This is the seam the module installer needs: a module
        // appears / disappears here without editing the shell. The shell's own General
        // stays a static anchor; Logs stays a footer command. Order leaves gaps so a
        // later module can land between two existing ones. Recording (order 50) sits
        // first in the band — right after General — where its former static anchor was;
        // Diagnostics (order 600) sits last, where its own former static anchor was.
        //
        // Each descriptor is captured once and registered twice: into the nav registry
        // that materialises its rail item, and into the search index paired with the
        // module's SettingsSearch.Entries — the page's findable cards, resolved from the
        // module's own PRI subtree without composing the page.
        // A page registers only when the presence module that owns it is
        // installed: an absent module has no rail entry and no search hits.
        // Both registrations travel together — a nav item without its search
        // entries (or the reverse) would read as a half-installed module.
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
        // Diagnostics is shell-level observability, not a presence module —
        // always registered.
        RegisterSettingsModule(Diagnostics.Logging.DiagnosticsSettingsModule.Describe(order: 600), Diagnostics.Logging.SettingsSearch.Entries);

        // General is the shell's one static nav anchor, not a registry module, so it has
        // no descriptor to read coordinates off: its cards register with the values its
        // NavigationViewItem carries in SettingsWindow.xaml — the Deckle.Settings.GeneralPage
        // tag, the Home glyph, and the nav label resolved from the shell's own subtree
        // (SettingsNavGeneral, the item's x:Uid).
        Settings.SettingsSearchIndex.RegisterPage(
            "Deckle.Settings.GeneralPage",
            "Deckle.Settings",
            Deckle.Catalog.Glyphs.Home,
            Deckle.Catalog.Loc.GetFrom("Deckle.Settings", "SettingsNavGeneral/Content"),
            Settings.SettingsSearch.Entries);
    }
}


using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Deckle.Catalog;
using Deckle.Settings;
using Deckle.Core;
using Deckle.Shell;

namespace Deckle.Settings;

public sealed partial class GeneralPage : Page
{
    public GeneralViewModel ViewModel { get; } = new();

    public GeneralPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ComposeAppearanceSection();
        ComposeBehaviourSection();
        ComposeStartupSection();
        ComposeApplicationDataSection();
        LoadAndSync();
    }

    // ── Composed Appearance section ───────────────────────────────────────────
    //
    // Host-only, like the Behaviour section below: the page hands the host panel
    // and the ViewModel's Appearance manifest to the composer, which builds the
    // theme ComboBox (a Choice descriptor). The composer subscribes to the
    // ViewModel, so the picker reflects Load() and the section "Reset" with no
    // SyncThemeCombo pass here — the old hand-wired ThemeCombo handler and its
    // sync method are gone. Composed before LoadAndSync so the subscription
    // catches Load()'s PropertyChanged; held in a field so it lives as long as
    // the (cached) page.
    private SettingsComposer? _appearanceComposer;

    private void ComposeAppearanceSection()
    {
        _appearanceComposer = new SettingsComposer(AppearanceHost, ViewModel);
        // Gate the section "Reset" link on the composer's dirtiness, re-evaluated
        // each time it raises DirtyChanged (after every RefreshAll). Subscribed
        // before Compose so its closing RefreshAll lands the initial gate too.
        _appearanceComposer.DirtyChanged += (_, _) =>
            AppearanceResetLink.IsEnabled = _appearanceComposer.IsDirty();
        _appearanceComposer.Compose(ViewModel.AppearanceSettings);
    }

    // ── Composed Behaviour section ────────────────────────────────────────────
    //
    // The page only hosts: it hands the host panel and the ViewModel's settings
    // manifest (declared beside the VM in GeneralViewModel.Settings.cs) to the
    // composer, which builds the cards. The manifest now carries the overlay
    // Group (master toggle + fade/animations/position children) ahead of the flat
    // auto-paste toggle, so the whole section is composed — the hand-authored
    // overlay SettingsExpander is gone. The composer subscribes to the ViewModel
    // so the controls reflect Load() and the section "Reset" without any binding
    // here. Composed before LoadAndSync so the subscription catches Load()'s
    // PropertyChanged. Held in a field so the subscription lives as long as the
    // (cached) page.
    private SettingsComposer? _behaviourComposer;

    private void ComposeBehaviourSection()
    {
        _behaviourComposer = new SettingsComposer(BehaviourHost, ViewModel);
        _behaviourComposer.DirtyChanged += (_, _) =>
            BehaviourResetLink.IsEnabled = _behaviourComposer.IsDirty();
        _behaviourComposer.Compose(ViewModel.BehaviourSettings);
    }

    // ── Composed Startup section ──────────────────────────────────────────────
    //
    // Same host-only pattern: the "start with Windows" toggle, whose registry
    // write and revert-on-refusal live in the VM setter, composes like any toggle.
    // No code-behind sync for it — the composer's subscription reflects Load(),
    // the section Reset, and the setter's own revert via PropertyChanged.
    private SettingsComposer? _startupComposer;

    private void ComposeStartupSection()
    {
        _startupComposer = new SettingsComposer(StartupHost, ViewModel);
        _startupComposer.DirtyChanged += (_, _) =>
            StartupResetLink.IsEnabled = _startupComposer.IsDirty();
        _startupComposer.Compose(ViewModel.StartupSettings);
    }

    // ── Composed backup-location card ─────────────────────────────────────────
    //
    // Same host-only pattern for the one settable value under "Application data":
    // the backup-location folder picker, a Path descriptor whose DefaultPath (the
    // AppPaths fallback the code-behind used to push) now rides its PathArgs. The
    // composer's per-card reset replaces the old code-behind SyncFolderPickerDefaults
    // wiring; the picker reflects Load() through the composer's subscription. The
    // "Application data" section has no hand-authored section-reset link, so there is
    // no DirtyChanged gating here — the composed card carries its own inline reset.
    private SettingsComposer? _applicationDataComposer;

    private void ComposeApplicationDataSection()
    {
        _applicationDataComposer = new SettingsComposer(BackupLocationHost, ViewModel);
        _applicationDataComposer.Compose(ViewModel.ApplicationDataSettings);
    }

    // NavigationCacheMode.Required reuses the page instance — the constructor
    // and Loaded only fire once. Without this override, navigating away then
    // back would show stale values, and PushToSettings() (which writes ALL VM
    // properties) would silently overwrite any changes made from another page.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadAndSync();
    }

    // Refills the VM from settings and refreshes the page's few hand-authored
    // readouts. The composed sections (Appearance, Behaviour, Startup) need no
    // sync pass: each composer subscribes to the VM, so Load()'s PropertyChanged
    // re-syncs their controls. The old _initializing guard (and its deferred
    // DispatcherQueue release) is gone with the last hand-wired combo handler.
    private void LoadAndSync()
    {
        ViewModel.Load();
        DataFolderPathText.Text = AppPaths.UserDataRoot;
    }

    // ── Reset per section ───────────────────────────────────────────────────
    //
    // Each section "Reset" link drives its composer's ResetAll(): every defaulted
    // card and group goes back to its POCO default through its own setter, which
    // raises PropertyChanged and re-syncs the surface (and re-gates the link via
    // DirtyChanged). The value-setting side effects ride the setters — Appearance's
    // ApplyTheme in OnThemeChanged, Startup's registry write in
    // OnAutostartEnabledChanged — so the handler only triggers the reset and keeps
    // the section-reset log line the VM methods used to emit.

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        _appearanceComposer?.ResetAll();
        DeckleSettingsSource.Log.SectionReset();
        DeckleSettingsSource.Log.SectionResetDetail("Appearance");
    }

    private void ResetBehaviour_Click(object sender, RoutedEventArgs e)
    {
        _behaviourComposer?.ResetAll();
        DeckleSettingsSource.Log.SectionReset();
        DeckleSettingsSource.Log.SectionResetDetail("Behaviour");
    }

    private void ResetStartup_Click(object sender, RoutedEventArgs e)
    {
        // The autostart toggle's default is off; ResetAll() sets it off, whose
        // setter calls StartupService.StopStartup() — the vehicle removal rides
        // along, exactly as a manual toggle would, so no direct call here anymore.
        _startupComposer?.ResetAll();
        DeckleSettingsSource.Log.SectionReset();
        DeckleSettingsSource.Log.SectionResetDetail("Startup");
    }

    // Opens the UserDataRoot in File Explorer — entry point for users who
    // want to inspect, back up, or wipe everything mutable the app stores.
    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string path = AppPaths.UserDataRoot;

        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.FolderPickerFailed();
            DeckleSettingsSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    // Re-opens the first-run wizard on demand. Used to swap the Whisper
    // model (Browse + download) or replace the native runtime without
    // wiping <UserDataRoot>. The wizard runs detached from the Settings
    // window — Settings stays open behind it.
    private void ReRunSetupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // The first-run wizard lives in the standalone Deckle.Setup
            // module. Going through the SettingsHost hook keeps
            // Deckle.Settings free of a back-reference to Deckle.exe.
            if (SettingsHost.OpenSetupWizard is null)
            {
                DeckleSettingsSource.Log.SetupWizardHookNotWired();
                return;
            }
            SettingsHost.OpenSetupWizard.Invoke();
            DeckleSettingsSource.Log.SetupWindowOpenedFromSettings();
        }
        catch (Exception ex)
        {
            DeckleSettingsSource.Log.SetupWindowOpenFailed();
            DeckleSettingsSource.Log.SetupWindowOpenFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    // ── Backup ──────────────────────────────────────────────────────────────
    //
    // PowerToys-style: a single SettingsExpander, two header actions
    // (Back up / Restore), and a folder picker for the location. Restore
    // targets the latest snapshot — older ones live in the folder and are
    // restorable by hand if ever needed. The location folder picker is now the
    // composed Path card (ComposeApplicationDataSection); its DefaultPath and
    // per-card reset ride the descriptor's PathArgs/Default. The Create/Restore
    // handlers below stay hand-authored — they are actions, not settings.

    private void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsBackupService.CreateBackup();
        ViewModel.RefreshBackups();
    }

    private async void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var latest = ViewModel.LatestBackup;
        if (latest is null)
        {
            DeckleSettingsSource.Log.RestoreSkippedSnapshotMissing();
            DeckleSettingsSource.Log.RestoreSkippedSnapshotMissingDetail("(no_backup)");
            return;
        }

        // Restoring overwrites the live settings.json with the snapshot — an
        // irreversible swap, so it goes through the shared destructive-confirm
        // gate (Close is the default button; Enter does nothing). Same three
        // restore strings as before; the service owns only the Cancel verb.
        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_RestoreDialog_Title"),
                Loc.Format("Settings_RestoreDialog_Content_Format", latest.DisplayName),
                Loc.Get("Settings_RestoreDialog_PrimaryButton"),
                IsDestructive: true));
        if (!confirmed) return;

        bool ok = SettingsBackupService.RestoreFromBackup(latest.Path);
        if (!ok) return;

        // Settings have been replaced and SettingsService.Reload has fired
        // Changed. Refill the VM from the new in-memory snapshot. Other
        // pages (RecordingPage, DiagnosticsPage, WhisperPage) will refill
        // their own VMs on next OnNavigatedTo via NavigationCacheMode.
        ViewModel.Load();

        // The Appearance composer re-selects the theme ComboBox off the Load()
        // PropertyChanged; we still apply the theme side-effect explicitly,
        // since Load() runs under the VM's sync guard which suppresses it.
        // Apply theme side-effect beyond the VM — RecordingViewModel
        // owns the level-window mapper push so we don't touch it from
        // here anymore.
        SettingsHost.ApplyTheme?.Invoke(ViewModel.Theme);
    }
}

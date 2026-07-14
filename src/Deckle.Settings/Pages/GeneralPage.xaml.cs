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
using Deckle.Diagnostics;

namespace Deckle.Settings;

public sealed partial class GeneralPage : Page
{
    public GeneralViewModel ViewModel { get; } = new();

    public GeneralPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        ComposeAppearanceSection();
        ComposeStartupSection();
        ComposeUpdatesSection();
        ComposeApplicationDataSection();

        // The page-level "Reset all" gate spans every section composer; re-gate it
        // whenever any goes dirty (each section link keeps its own gating too).
        foreach (var composer in new[]
                 { _appearanceComposer, _startupComposer, _updatesComposer, _applicationDataComposer })
            composer!.DirtyChanged += (_, _) => GateResetAll();

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

    // ── Composed Updates section ──────────────────────────────────────────────
    //
    // Same host-only pattern: the silent-check opt-out toggle. The version
    // readout below it stays hand-authored (a status projection plus an
    // action button, refreshed by LoadAndSync through the SettingsHost hooks).
    private SettingsComposer? _updatesComposer;

    private void ComposeUpdatesSection()
    {
        _updatesComposer = new SettingsComposer(UpdatesHost, ViewModel);
        _updatesComposer.DirtyChanged += (_, _) =>
            UpdatesResetLink.IsEnabled = _updatesComposer.IsDirty();
        _updatesComposer.Compose(ViewModel.UpdatesSettings);
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
    // readouts. The composed sections (Appearance, Startup) need no
    // sync pass: each composer subscribes to the VM, so Load()'s PropertyChanged
    // re-syncs their controls. The old _initializing guard (and its deferred
    // DispatcherQueue release) is gone with the last hand-wired combo handler.
    private void LoadAndSync()
    {
        ViewModel.Load();
        DataFolderPathText.Text = AppPaths.UserDataRoot;
        // The move rides the App's relocate hook; unwired (tests, partial
        // hosts) the affordance simply isn't there.
        MoveDataFolderButton.Visibility = SettingsHost.RelocateDataRoot is null
            ? Visibility.Collapsed : Visibility.Visible;
        RefreshVersionCard();
        // Settle the page-reset gate off the freshly-loaded values — Load() may raise
        // no PropertyChanged on a clean profile, so no composer DirtyChanged would fire.
        GateResetAll();
    }

    // The version row reads through the SettingsHost hooks: the running build's
    // version as the card description, and — when the silent check has parked a
    // newer release — the offer text plus the "Install now" action. Unwired
    // hooks (tests, partial hosts) leave a bare version row.
    private void RefreshVersionCard()
    {
        VersionCard.Description = SettingsHost.GetAppVersion?.Invoke() ?? "";

        string? available = SettingsHost.GetAvailableUpdateVersion?.Invoke();
        bool hasUpdate = available is not null && SettingsHost.StartUpdate is not null;
        UpdateAvailableText.Text = hasUpdate
            ? Loc.Format("GeneralUpdateAvailableLabel_Format", available!)
            : "";
        UpdateAvailableText.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsHost.StartUpdate?.Invoke();
    }

    // ── Whole-page "Reset all" ────────────────────────────────────────────────
    //
    // Drives every section's composer back to its defaults at once — the same
    // ResetAll() each section link fires, folded into one gesture on the title row.
    // Active-when-dirty (the Playground model): enabled only while some composed value
    // differs from its default, re-gated off every composer's DirtyChanged and once
    // after Load(). No confirmation — these are reversible preference defaults
    // (appearance, behaviour, startup, backup location), nothing user-authored is
    // lost, matching RecordingPage's section reset.
    private void GateResetAll()
    {
        ResetAllButton.IsEnabled =
            (_appearanceComposer?.IsDirty() ?? false) ||
            (_startupComposer?.IsDirty() ?? false) ||
            (_updatesComposer?.IsDirty() ?? false) ||
            (_applicationDataComposer?.IsDirty() ?? false);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _appearanceComposer?.ResetAll();
        _startupComposer?.ResetAll();
        _updatesComposer?.ResetAll();
        _applicationDataComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("General (all)");
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
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Appearance");
    }

    private void ResetStartup_Click(object sender, RoutedEventArgs e)
    {
        // The autostart toggle's default is off; ResetAll() sets it off, whose
        // setter calls StartupService.StopStartup() — the vehicle removal rides
        // along, exactly as a manual toggle would, so no direct call here anymore.
        _startupComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Startup");
    }

    private void ResetUpdates_Click(object sender, RoutedEventArgs e)
    {
        _updatesComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Updates");
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
            DeckleSettingsUxSource.Log.FolderPickerFailed();
            DeckleSettingsUxSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    // ── Data-root move ──────────────────────────────────────────────────────
    //
    // The page owns the pre-flight only: pick a target, normalize it (a
    // non-empty pick lands in a Deckle subfolder, so the later cleanup never
    // entangles foreign files), refuse nesting and same-target, gate on the
    // target drive's free space, then confirm — the restart makes this a
    // held-until-confirmed action. The actual move runs in the dedicated
    // relocate process behind SettingsHost.RelocateDataRoot, which re-checks
    // space authoritatively before copying.

    private async void MoveDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsHost.RelocateDataRoot is null) return;

        try
        {
            var window = SettingsHost.GetSettingsWindow?.Invoke()
                ?? throw new InvalidOperationException("Settings window not initialized");
            FolderPickerCard.EmitFolderPickerAnchor(sender as FrameworkElement, window);

            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
            };
            var result = await picker.PickSingleFolderAsync();
            if (result is null) return;

            // Transient busy while the size scan runs — a data root can hold
            // gigabytes across thousands of files.
            MoveDataFolderButton.IsEnabled = false;
            try { await BeginDataMoveAsync(result.Path); }
            finally { MoveDataFolderButton.IsEnabled = true; }
        }
        catch (Exception ex)
        {
            DeckleSettingsUxSource.Log.FolderPickerFailed();
            DeckleSettingsUxSource.Log.FolderPickerFailedDetail(ex.GetType().Name, ex.Message);
        }
    }

    private async Task BeginDataMoveAsync(string picked)
    {
        DataMoveInfoBar.IsOpen = false;

        string current = System.IO.Path.GetFullPath(AppPaths.UserDataRoot);
        string target  = System.IO.Path.GetFullPath(picked);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()
            && !PathsEqual(target, current))
            target = System.IO.Path.Combine(target, "Deckle");

        if (PathsEqual(target, current))
        {
            ShowMoveOutcome(InfoBarSeverity.Informational, Loc.Get("Settings_MoveDataSameTarget"));
            return;
        }
        if (IsNested(target, current) || IsNested(current, target)
            || (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()))
        {
            ShowMoveOutcome(InfoBarSeverity.Error, Loc.Get("Settings_MoveDataInvalidTarget"));
            return;
        }

        long required = await Task.Run(() =>
        {
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(current, "*", System.IO.SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
            return total;
        });
        var drive = new DriveInfo(System.IO.Path.GetPathRoot(target)!);
        if (drive.AvailableFreeSpace < required)
        {
            ShowMoveOutcome(InfoBarSeverity.Error, Loc.Format(
                "Settings_MoveDataInsufficientSpace_Format",
                drive.Name, FormatBytes(required), FormatBytes(drive.AvailableFreeSpace)));
            return;
        }

        bool confirmed = await ConfirmationService.RequestAsync(
            this.XamlRoot,
            new ConfirmationRequest(
                Loc.Get("Settings_MoveDataDialog_Title"),
                Loc.Format("Settings_MoveDataDialog_Content_Format", FormatBytes(required), target),
                Loc.Get("Settings_MoveDataDialog_PrimaryButton")));
        if (!confirmed) return;

        SettingsHost.RelocateDataRoot!.Invoke(target);
    }

    private void ShowMoveOutcome(InfoBarSeverity severity, string message)
    {
        DataMoveInfoBar.Severity = severity;
        DataMoveInfoBar.Message = message;
        DataMoveInfoBar.IsOpen = true;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            System.IO.Path.GetFullPath(a).TrimEnd('\\'),
            System.IO.Path.GetFullPath(b).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsNested(string child, string parent) =>
        System.IO.Path.GetFullPath(child).TrimEnd('\\')
            .StartsWith(System.IO.Path.GetFullPath(parent).TrimEnd('\\') + "\\",
                StringComparison.OrdinalIgnoreCase);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)               return $"{bytes} B";
        if (bytes < 1024L * 1024)       return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F0} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
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

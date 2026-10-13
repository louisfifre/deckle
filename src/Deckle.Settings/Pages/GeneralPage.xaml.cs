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

        ComposeHotkeysSection();
        ComposeAppearanceSection();
        ComposeStartupSection();
        ComposeUpdatesSection();
        ComposeApplicationDataSection();

        foreach (var composer in new[]
                 { _hotkeysComposer, _appearanceComposer, _startupComposer, _updatesComposer, _applicationDataComposer })
            composer!.DirtyChanged += (_, _) => GateResetAll();

        LoadAndSync();
    }

    // ── Composed Hotkeys section ──────────────────────────────────────────────
    //
    // The App rebinds the global hotkeys itself when the saved settings change.
    private SettingsComposer? _hotkeysComposer;

    private void ComposeHotkeysSection()
    {
        _hotkeysComposer = new SettingsComposer(HotkeysHost, ViewModel);
        _hotkeysComposer.DirtyChanged += (_, _) =>
            HotkeysResetLink.IsEnabled = _hotkeysComposer.IsDirty();
        _hotkeysComposer.Compose(ViewModel.HotkeySettings);
    }

    // ── Composed Appearance section ───────────────────────────────────────────
    //
    // Every section composer is built before LoadAndSync so its subscription
    // catches Load()'s PropertyChanged, and held in a field so it lives as long
    // as the (cached) page.
    private SettingsComposer? _appearanceComposer;

    private void ComposeAppearanceSection()
    {
        _appearanceComposer = new SettingsComposer(AppearanceHost, ViewModel);
        // Subscribed before Compose so its closing RefreshAll lands the initial
        // gate too.
        _appearanceComposer.DirtyChanged += (_, _) =>
            AppearanceResetLink.IsEnabled = _appearanceComposer.IsDirty();
        _appearanceComposer.Compose(ViewModel.AppearanceSettings);
    }

    // ── Composed Startup section ──────────────────────────────────────────────
    private SettingsComposer? _startupComposer;

    private void ComposeStartupSection()
    {
        _startupComposer = new SettingsComposer(StartupHost, ViewModel);
        _startupComposer.DirtyChanged += (_, _) =>
            StartupResetLink.IsEnabled = _startupComposer.IsDirty();
        _startupComposer.Compose(ViewModel.StartupSettings);
    }

    // ── Composed Updates section ──────────────────────────────────────────────
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
    // "Application data" has no section-reset link, so no DirtyChanged gating
    // here; the composed card carries its own inline reset.
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

    // The composed sections need no sync pass: each composer subscribes to the
    // VM, so Load()'s PropertyChanged re-syncs their controls.
    private void LoadAndSync()
    {
        ViewModel.Load();
        DataFolderPathText.Text = AppPaths.UserDataRoot;
        MoveDataFolderButton.Visibility = SettingsHost.RelocateDataRoot is null
            ? Visibility.Collapsed : Visibility.Visible;
        RefreshVersionCard();
        // Settle the page-reset gate off the freshly-loaded values — Load() may raise
        // no PropertyChanged on a clean profile, so no composer DirtyChanged would fire.
        GateResetAll();
    }

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
    // No confirmation: these are reversible preference defaults, nothing
    // user-authored is lost.
    private void GateResetAll()
    {
        ResetAllButton.IsEnabled =
            (_hotkeysComposer?.IsDirty() ?? false) ||
            (_appearanceComposer?.IsDirty() ?? false) ||
            (_startupComposer?.IsDirty() ?? false) ||
            (_updatesComposer?.IsDirty() ?? false) ||
            (_applicationDataComposer?.IsDirty() ?? false);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _hotkeysComposer?.ResetAll();
        _appearanceComposer?.ResetAll();
        _startupComposer?.ResetAll();
        _updatesComposer?.ResetAll();
        _applicationDataComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("General (all)");
    }

    // ── Reset per section ───────────────────────────────────────────────────
    //
    // The side effects ride the VM setters (ApplyTheme in OnThemeChanged, the
    // startup write in OnAutostartEnabledChanged), so a handler only triggers the
    // composer's ResetAll() and logs.

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _hotkeysComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Hotkeys");
    }

    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        _appearanceComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Appearance");
    }

    private void ResetStartup_Click(object sender, RoutedEventArgs e)
    {
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
    // The page owns the pre-flight only; the move itself runs in the relocate
    // process behind SettingsHost.RelocateDataRoot, which re-checks space before
    // copying. A non-empty pick lands in a Deckle subfolder, so the later cleanup
    // never entangles foreign files.

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

    private void ReRunSetupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
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
    // Restore targets the latest snapshot; older ones stay in the folder,
    // restorable by hand.

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
        // gate (Close is the default button; Enter does nothing).
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

        ViewModel.Load();

        // Load() runs under the VM's sync guard, which suppresses the theme
        // side effect, so apply it explicitly.
        SettingsHost.ApplyTheme?.Invoke(ViewModel.Theme);
    }
}

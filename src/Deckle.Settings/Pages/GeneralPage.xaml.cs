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

        foreach (var composer in new[]
                 { _appearanceComposer, _startupComposer, _updatesComposer, _applicationDataComposer })
            composer!.DirtyChanged += (_, _) => GateResetAll();

        LoadAndSync();
    }

    // ── Composed Appearance section ───────────────────────────────────────────
    //
    // Every composer is created before LoadAndSync so its subscription catches
    // Load()'s PropertyChanged, and held in a field so it lives as long as the
    // cached page.
    private SettingsComposer? _appearanceComposer;

    private void ComposeAppearanceSection()
    {
        _appearanceComposer = new SettingsComposer(AppearanceHost, ViewModel);
        // Subscribed before Compose so its closing RefreshAll lands the initial
        // gate too.
        _appearanceComposer.DirtyChanged += (_, _) =>
            AppearanceSection.IsResetEnabled = _appearanceComposer.IsDirty();
        _appearanceComposer.Compose(ViewModel.AppearanceSettings);
    }

    // ── Composed Startup section ──────────────────────────────────────────────
    //
    // The "start with Windows" toggle's registry write and revert-on-refusal live
    // in the VM setter, so it composes like any toggle: the composer's
    // subscription reflects the setter's own revert.
    private SettingsComposer? _startupComposer;

    private void ComposeStartupSection()
    {
        _startupComposer = new SettingsComposer(StartupHost, ViewModel);
        _startupComposer.DirtyChanged += (_, _) =>
            StartupSection.IsResetEnabled = _startupComposer.IsDirty();
        _startupComposer.Compose(ViewModel.StartupSettings);
    }

    // ── Composed Updates section ──────────────────────────────────────────────
    private SettingsComposer? _updatesComposer;

    private void ComposeUpdatesSection()
    {
        _updatesComposer = new SettingsComposer(UpdatesHost, ViewModel);
        _updatesComposer.DirtyChanged += (_, _) =>
            UpdatesSection.IsResetEnabled = _updatesComposer.IsDirty();
        _updatesComposer.Compose(ViewModel.UpdatesSettings);
    }

    // ── Composed backup-location card ─────────────────────────────────────────
    //
    // The "Application data" section has no section reset, so no section gating
    // here; the composed card carries its own inline reset.
    //
    // The card lives in the backup fold's Items, which accepts SettingsCards
    // only: the SettingsExpander applies its SettingsCard item style to every
    // item when the fold opens, and a StackPanel host there threw at that
    // moment (the crash of 2026-10-14 19:09). The composer needs a Panel, so
    // it composes into a panel of its own, then the cards move into the fold
    // ahead of the bespoke information card. The composer keeps driving them:
    // it holds the controls, not the panel.
    private SettingsComposer? _applicationDataComposer;

    private void ComposeApplicationDataSection()
    {
        var host = new StackPanel();
        _applicationDataComposer = new SettingsComposer(host, ViewModel);
        _applicationDataComposer.Compose(ViewModel.ApplicationDataSettings);

        for (int i = host.Children.Count - 1; i >= 0; i--)
        {
            UIElement card = host.Children[i];
            host.Children.RemoveAt(i);
            BackupExpander.Items.Insert(0, card);
        }
    }

    // NavigationCacheMode.Required reuses the page instance — the constructor
    // and Loaded only fire once. Without this override, navigating away then
    // back would show stale values, and PushToSettings() (which writes every
    // value this page shows) would silently overwrite a change made elsewhere,
    // such as the theme from the tray.
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
        // The move rides the App's relocate hook; unwired (tests, partial
        // hosts) the affordance simply isn't there.
        MoveDataFolderButton.Visibility = SettingsHost.RelocateDataRoot is null
            ? Visibility.Collapsed : Visibility.Visible;
        RefreshVersionCard();
        // Load() raises no PropertyChanged on a clean profile, so no composer
        // DirtyChanged fires and this call settles the gate.
        GateResetAll();
    }

    // Unwired SettingsHost hooks (tests, partial hosts) leave a bare version row.
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
    // The header block confirms by itself, with the generic wording: nothing
    // user-authored is lost here.
    private void GateResetAll()
    {
        Header.IsResetAllEnabled =
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
    // Each value goes back to its default through its own setter, which carries
    // the side effects (Appearance's ApplyTheme in OnThemeChanged, Startup's
    // registry write in OnAutostartEnabledChanged), so a handler only triggers
    // the reset and logs it.

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

    private void ReRunSetupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Going through the SettingsHost hook keeps Deckle.Settings free of
            // a back-reference to Deckle.exe.
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

        // Restoring overwrites the live settings.json with the snapshot, an
        // irreversible swap, so it goes through the destructive confirmation
        // (Close is the default button; Enter does nothing).
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

        // Other cached pages refill their own VMs on their next OnNavigatedTo.
        ViewModel.Load();

        // Load() runs under the VM's sync guard, which suppresses the theme side
        // effect, so it is applied here.
        SettingsHost.ApplyTheme?.Invoke(ViewModel.Theme);
    }
}

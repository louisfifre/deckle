using System;
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
        ComposeDataFolderSection();

        foreach (var composer in new[]
                 { _appearanceComposer, _startupComposer, _updatesComposer,
                   _applicationDataComposer, _dataFolderComposer })
            composer!.DirtyChanged += (_, _) => GateResetAll();

        ViewModel.DataFolderMoveRequested += OnDataFolderMoveRequested;

        LoadAndSync();
    }

    // ── Composed top cards: theme, then the two logon switches ────────────────
    //
    // Both compose into TopHost, under no section title (a card or two earns
    // no heading). Every composer is created before LoadAndSync so its
    // subscription catches Load()'s PropertyChanged, and held in a field so it
    // lives as long as the cached page.
    private SettingsComposer? _appearanceComposer;

    private void ComposeAppearanceSection()
    {
        _appearanceComposer = new SettingsComposer(TopHost, ViewModel);
        _appearanceComposer.Compose(ViewModel.AppearanceSettings);
    }

    // The "start with Windows" and "start elevated" toggles' OS writes and
    // revert-on-refusal live in the VM setters, so they compose like any
    // toggle: the composer's subscription reflects the setters' own reverts
    // and the way each one rides onto the other.
    private SettingsComposer? _startupComposer;

    private void ComposeStartupSection()
    {
        _startupComposer = new SettingsComposer(TopHost, ViewModel);
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

    // ── Composed backup Location line ─────────────────────────────────────────
    //
    // The "Application data" section holds two composers, this line and the
    // data folder fold below, so its section reset is gated off both
    // (GateApplicationDataSection). The line also keeps its own inline reset.
    //
    // The line lives in the backup fold's Items, which accepts SettingsCards
    // only: the SettingsExpander applies its SettingsCard item style to every
    // item when the fold opens, and a StackPanel host there threw at that
    // moment (the crash of 2026-10-14 19:09). The composer needs a Panel, so
    // it composes into a panel of its own, as a fold's child (inFold: a Path
    // is then the Location line alone, a SettingsCard, where standing alone it
    // would be a SettingsExpander, which Items refuses), then the cards move
    // into the fold ahead of the bespoke information card. The composer keeps
    // driving them: it holds the controls, not the panel.
    private SettingsComposer? _applicationDataComposer;

    private void ComposeApplicationDataSection()
    {
        var host = new StackPanel();
        _applicationDataComposer = new SettingsComposer(host, ViewModel);
        _applicationDataComposer.DirtyChanged += (_, _) => GateApplicationDataSection();
        _applicationDataComposer.Compose(ViewModel.ApplicationDataSettings, inFold: true);

        for (int i = host.Children.Count - 1; i >= 0; i--)
        {
            UIElement card = host.Children[i];
            host.Children.RemoveAt(i);
            BackupExpander.Items.Insert(0, card);
        }
    }

    // ── Composed data folder fold ─────────────────────────────────────────────
    //
    // Standing alone in its host, the Path renders as the folder fold. Its
    // "Choose a folder" relays the pick to the VM, which raises
    // DataFolderMoveRequested; the pre-flight below answers it.
    private SettingsComposer? _dataFolderComposer;

    private void ComposeDataFolderSection()
    {
        _dataFolderComposer = new SettingsComposer(DataFolderHost, ViewModel);
        _dataFolderComposer.DirtyChanged += (_, _) => GateApplicationDataSection();
        _dataFolderComposer.Compose(ViewModel.DataFolderSettings);
    }

    // The backup line is composed first, so the data folder composer is still
    // null on the line's first DirtyChanged and counts as clean until its own.
    private void GateApplicationDataSection()
    {
        ApplicationDataSection.IsResetEnabled =
            (_applicationDataComposer?.IsDirty() ?? false) ||
            (_dataFolderComposer?.IsDirty() ?? false);
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
            (_applicationDataComposer?.IsDirty() ?? false) ||
            (_dataFolderComposer?.IsDirty() ?? false);
    }

    // The data folder goes last. Its reset is a move back to the default root,
    // which asks its own confirmation and restarts the app, so every other
    // value is already back to its default and saved in the root being moved.
    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _appearanceComposer?.ResetAll();
        _startupComposer?.ResetAll();
        _updatesComposer?.ResetAll();
        _applicationDataComposer?.ResetAll();
        _dataFolderComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("General (all)");
    }

    // ── Reset per section ───────────────────────────────────────────────────
    //
    // Each value goes back to its default through its own setter, which carries
    // the side effects (the theme's ApplyTheme in OnThemeChanged, the startup
    // registry write in OnAutostartEnabledChanged), so a handler only triggers
    // the reset and logs it. The top cards have no section, so no section
    // reset: each keeps its own, and Reset all covers them.

    private void ResetUpdates_Click(object sender, RoutedEventArgs e)
    {
        _updatesComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("Updates");
    }

    // The same order as Reset all, for the same reason: the backup location
    // goes back first and is saved, then the data folder's reset starts the
    // move back to the default root. That move asks its own confirmation and
    // restarts the app, so the section wheel asks nothing before it.
    private void ResetApplicationData_Click(object sender, RoutedEventArgs e)
    {
        _applicationDataComposer?.ResetAll();
        _dataFolderComposer?.ResetAll();
        DeckleSettingsUxSource.Log.SectionReset();
        DeckleSettingsUxSource.Log.SectionResetDetail("ApplicationData");
    }

    // ── Data-root move ──────────────────────────────────────────────────────
    //
    // The folder dialog belongs to the Location line (FolderLocationLine); the
    // page owns the pre-flight only: normalize the pick (a non-empty pick lands
    // in a Deckle subfolder, so the later cleanup never entangles foreign
    // files), refuse nesting and same-target, gate on the target drive's free
    // space, then confirm — the restart makes this a held-until-confirmed
    // action. The actual move runs in the dedicated relocate process behind
    // SettingsHost.RelocateDataRoot, which re-checks space authoritatively
    // before copying.

    private async void OnDataFolderMoveRequested(object? sender, string picked)
    {
        if (SettingsHost.RelocateDataRoot is null) return;

        // Transient busy while the size scan runs — a data root can hold
        // gigabytes across thousands of files. The VM flag greys the line.
        ViewModel.DataFolderMoveInFlight = true;
        try
        {
            await BeginDataMoveAsync(picked);
        }
        catch (Exception ex)
        {
            DeckleSettingsUxSource.Log.FolderActionFailed("relocate", ex.GetType().Name, ex.HResult, ex.Message);
        }
        finally
        {
            ViewModel.DataFolderMoveInFlight = false;
        }
    }

    private async Task BeginDataMoveAsync(string picked)
    {
        DataMoveInfoBar.IsOpen = false;

        string current = System.IO.Path.GetFullPath(AppPaths.UserDataRoot);
        string target  = System.IO.Path.GetFullPath(picked);

        // The default root is the reset's target and is taken as it is: a Deckle
        // subfolder of it would be another custom root, not the default. If it
        // still holds files, the nesting check below refuses it like any
        // occupied folder.
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()
            && !PathsEqual(target, current)
            && !PathsEqual(target, AppPaths.DefaultUserDataRoot))
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
            int skipped = 0;
            Exception? lastSkip = null;
            foreach (string file in Directory.EnumerateFiles(current, "*", System.IO.SearchOption.AllDirectories))
            {
                // A file that vanished or is locked mid-walk only lowers the
                // estimate; the move itself copies what is really there.
                try { total += new FileInfo(file).Length; }
                catch (Exception ex) { skipped++; lastSkip = ex; }
            }
            if (lastSkip is not null)
                DeckleSettingsSource.Log.DataSizeFilesSkippedDetail(
                    skipped, lastSkip.GetType().Name, lastSkip.Message);
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
            DeckleSettingsSource.Log.SetupWindowOpenFailed(ex.GetType().Name, ex.Message);
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
        if (!confirmed)
        {
            DeckleSettingsSource.Log.RestoreDeclined();
            return;
        }

        bool ok = SettingsBackupService.RestoreFromBackup(latest.Path);
        if (!ok) return;

        // Other cached pages refill their own VMs on their next OnNavigatedTo.
        ViewModel.Load();

        // Load() runs under the VM's sync guard, which suppresses the theme side
        // effect, so it is applied here.
        SettingsHost.ApplyTheme?.Invoke(ViewModel.Theme);
    }
}

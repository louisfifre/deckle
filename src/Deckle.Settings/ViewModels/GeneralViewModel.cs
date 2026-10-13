using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Catalog;
using Deckle.Shell;
using Deckle.Core;
using Deckle.Diagnostics;

namespace Deckle.Settings;

// Load() pulls from the POCO, property changes push back via PushToSettings().
// The _isSyncing flag prevents re-saving during Load().
//
// Partial properties (not fields) for WinRT/AOT compatibility (MVVMTK0045).
public partial class GeneralViewModel : ObservableObject
{
    private bool _isSyncing;

    // ── Appearance ───────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial string Theme { get; set; }

    partial void OnThemeChanged(string value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Theme", value);
        PushToSettings();
        SettingsHost.ApplyTheme?.Invoke(value);
    }

    // ── Hotkeys ──────────────────────────────────────────────────────────────
    //
    // One chord per hotkey action, null = unbound. The composer writes only
    // complete chords or null, so every value pushed here is registrable; the
    // App rebinds when SettingsService.Changed reports a different binding set.

    [ObservableProperty]
    public partial HotkeyChord? RecordHotkey { get; set; }

    [ObservableProperty]
    public partial HotkeyChord? AmbientLightHotkey { get; set; }

    [ObservableProperty]
    public partial HotkeyChord? AutocorrectHotkey { get; set; }

    [ObservableProperty]
    public partial HotkeyChord? TaskbarCoverHotkey { get; set; }

    [ObservableProperty]
    public partial HotkeyChord? PrecisionScrollHotkey { get; set; }

    [ObservableProperty]
    public partial HotkeyChord? RewriteHotkey { get; set; }

    [ObservableProperty]
    public partial HotkeyChord? TrackpadHotkey { get; set; }

    partial void OnRecordHotkeyChanged(HotkeyChord? value) => OnHotkeyChanged("Hotkeys.Record", value);
    partial void OnAmbientLightHotkeyChanged(HotkeyChord? value) => OnHotkeyChanged("Hotkeys.AmbientLight", value);
    partial void OnAutocorrectHotkeyChanged(HotkeyChord? value) => OnHotkeyChanged("Hotkeys.Autocorrect", value);
    partial void OnTaskbarCoverHotkeyChanged(HotkeyChord? value) => OnHotkeyChanged("Hotkeys.TaskbarCover", value);
    partial void OnPrecisionScrollHotkeyChanged(HotkeyChord? value) => OnHotkeyChanged("Hotkeys.PrecisionScroll", value);
    partial void OnRewriteHotkeyChanged(HotkeyChord? value) => OnHotkeyChanged("Hotkeys.Rewrite", value);
    partial void OnTrackpadHotkeyChanged(HotkeyChord? value) => OnHotkeyChanged("Hotkeys.Trackpad", value);

    private void OnHotkeyChanged(string name, HotkeyChord? value)
    {
        if (_isSyncing) return;
        // Modifier flags and scancode only: no typed text reaches the log.
        DeckleSettingsUxSource.Log.SettingChanged(
            name, value is { } c ? $"{c.Modifiers}+0x{c.ScanCode:X}" : "unbound");
        PushToSettings();
    }

    // ── Startup ──────────────────────────────────────────────────────────────

    // AutostartEnabled is not backed by AppSettings — the source of truth is the
    // OS itself, across both logon vehicles (the HKCU\Run value and the elevated
    // scheduled task). It means "Deckle starts at logon", whichever vehicle
    // carries it, so it goes through StartupService rather than a single vehicle:
    // Load() probes both, the setter starts the default vehicle or stops every
    // vehicle. A refused write (GPO/ACL, missing ProcessPath, declined UAC on the
    // elevated task) reverts the toggle so it matches what starts at logon.
    [ObservableProperty]
    public partial bool AutostartEnabled { get; set; }

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        bool ok = value ? StartupService.StartStartup() : StartupService.StopStartup();
        if (ok)
        {
            DeckleSettingsUxSource.Log.SettingChanged("Start with Windows", value.ToString());
            return;
        }

        _isSyncing = true;
        try { AutostartEnabled = !value; }
        finally { _isSyncing = false; }
    }

    // ── Updates ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool UpdateAutoCheckEnabled { get; set; }

    partial void OnUpdateAutoCheckEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Updates.AutoCheckEnabled", value.ToString());
        PushToSettings();
    }

    // ── Backup ───────────────────────────────────────────────────────────────
    //
    // BackupDirectory is the user override for where snapshots live (empty =
    // AppPaths.SettingsBackupDirectory, see SettingsService.ResolveBackupDirectory).

    [ObservableProperty]
    public partial string BackupDirectory { get; set; }

    public ObservableCollection<BackupInfo> Backups { get; } = new();

    public BackupInfo? LatestBackup => Backups.Count > 0 ? Backups[0] : null;

    public bool HasBackup => LatestBackup is not null;

    public string LatestBackupFileName => LatestBackup is null
        ? "—"
        : Path.GetFileName(LatestBackup.Path);

    public string LatestBackupCreatedAt => LatestBackup is null
        ? "No backup yet"
        : LatestBackup.Timestamp.LocalDateTime.ToString("g");

    partial void OnBackupDirectoryChanged(string value)
    {
        if (_isSyncing) return;
        DeckleSettingsUxSource.Log.SettingChanged("Paths.BackupDirectory", $"\"{value}\"");
        PushToSettings();
        RefreshBackups();
    }

    public void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in SettingsBackupService.ListBackups())
            Backups.Add(b);

        OnPropertyChanged(nameof(LatestBackup));
        OnPropertyChanged(nameof(HasBackup));
        OnPropertyChanged(nameof(LatestBackupFileName));
        OnPropertyChanged(nameof(LatestBackupCreatedAt));
    }

    // ── Sync with SettingsService ────────────────────────────────────────────

    public GeneralViewModel()
    {
        _isSyncing = true;

        // Seed from the POCO initializers, the single source of truth for defaults
        // shared with the manifest's reset selectors: no literal default is spelled
        // here. Load() overwrites these; this only covers the gap before it.
        var appearance = new AppearanceSettings();
        var paths = new PathsSettings();
        var updates = new UpdatesSettings();

        Theme = appearance.Theme;
        // Autostart is OS-backed (registry + scheduled task), not an AppSettings
        // POCO — its conceptual default lives on the startup facade.
        AutostartEnabled = StartupService.DefaultEnabled;
        BackupDirectory = paths.BackupDirectory;
        UpdateAutoCheckEnabled = updates.AutoCheckEnabled;
        LoadHotkeys(new HotkeysSettings());

        // _isSyncing stays true — Load() will set it to false.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var shell = SettingsService.Instance.Current;
            Theme = shell.Appearance.Theme;
            AutostartEnabled = StartupService.StartsAtLogon();
            BackupDirectory = shell.Paths.BackupDirectory;
            UpdateAutoCheckEnabled = shell.Updates.AutoCheckEnabled;
            LoadHotkeys(shell.Hotkeys);
        }
        finally
        {
            _isSyncing = false;
        }

        RefreshBackups();
    }

    private void PushToSettings()
    {
        var shell = SettingsService.Instance.Current;
        shell.Appearance.Theme = Theme;
        shell.Paths.BackupDirectory = BackupDirectory ?? "";
        shell.Updates.AutoCheckEnabled = UpdateAutoCheckEnabled;
        shell.Hotkeys.Record = RecordHotkey;
        shell.Hotkeys.AmbientLight = AmbientLightHotkey;
        shell.Hotkeys.Autocorrect = AutocorrectHotkey;
        shell.Hotkeys.TaskbarCover = TaskbarCoverHotkey;
        shell.Hotkeys.PrecisionScroll = PrecisionScrollHotkey;
        shell.Hotkeys.Rewrite = RewriteHotkey;
        shell.Hotkeys.Trackpad = TrackpadHotkey;
        SettingsService.Instance.Save();
    }

    private void LoadHotkeys(HotkeysSettings hotkeys)
    {
        RecordHotkey = hotkeys.Record;
        AmbientLightHotkey = hotkeys.AmbientLight;
        AutocorrectHotkey = hotkeys.Autocorrect;
        TaskbarCoverHotkey = hotkeys.TaskbarCover;
        PrecisionScrollHotkey = hotkeys.PrecisionScroll;
        RewriteHotkey = hotkeys.Rewrite;
        TrackpadHotkey = hotkeys.Trackpad;
    }
}

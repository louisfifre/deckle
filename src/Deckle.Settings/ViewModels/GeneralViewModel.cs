using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.Shell;
using Deckle.Core;

namespace Deckle.Settings;

// ViewModel for GeneralPage — bridges shell-level AppSettings sections
// (Hotkeys, Appearance, Behaviour, Startup, Backup) to the XAML via x:Bind.
// Recording was extracted in slice S3 to RecordingViewModel ; Telemetry
// in slice S2 to DiagnosticsViewModel. Behaviour (auto-paste + overlay)
// was rapatriated here in pass2 — these are user-facing behaviors of the
// app as a whole, not Recording-page-specific settings.
//
// Pattern: Load() pulls from the POCO, property changes push back via
// PushToSettings(). The _isSyncing flag prevents re-saving during Load().
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
        DeckleSettingsSource.Log.SettingChanged("Theme", value);
        PushToSettings();
        SettingsHost.ApplyTheme?.Invoke(value);
    }

    // ── Behaviour ────────────────────────────────────────────────────────────
    //
    // Auto-paste : whether the transcript text is pasted into the focused
    // window after copy. Overlay : the on-screen HUD shown during recording
    // (master toggle + fade-on-proximity, animations, position). Both used
    // to live on the Recording page in slice S3 — moved here in pass2
    // because they describe the app's overall behaviour, not the capture
    // pipeline itself.
    //
    // Persistence stays in shell.Paste / shell.Overlay (settings.json).
    // The Recording page no longer reads or writes these.

    [ObservableProperty]
    public partial bool AutoPasteEnabled { get; set; }

    [ObservableProperty]
    public partial bool OverlayEnabled { get; set; }

    [ObservableProperty]
    public partial bool OverlayFadeOnProximity { get; set; }

    [ObservableProperty]
    public partial bool OverlayAnimations { get; set; }

    [ObservableProperty]
    public partial string OverlayPosition { get; set; }

    partial void OnAutoPasteEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("Auto-paste", value.ToString());
        PushToSettings();
    }

    partial void OnOverlayEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("Overlay enabled", value.ToString());
        PushToSettings();
    }

    partial void OnOverlayFadeOnProximityChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("Overlay fade", value.ToString());
        PushToSettings();
    }

    partial void OnOverlayAnimationsChanged(bool value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("Overlay animations", value.ToString());
        PushToSettings();
    }

    partial void OnOverlayPositionChanged(string value)
    {
        if (_isSyncing) return;
        DeckleSettingsSource.Log.SettingChanged("Overlay position", value);
        PushToSettings();
    }

    // ── Startup ──────────────────────────────────────────────────────────────

    // AutostartEnabled is not backed by AppSettings — the source of truth is the
    // OS itself, across both logon vehicles (the HKCU\Run value and the elevated
    // scheduled task). It means "Deckle starts at logon", whichever vehicle
    // carries it, so it goes through StartupService rather than a single vehicle:
    // Load() probes both, the setter starts the default vehicle or stops every
    // vehicle. If the write fails (GPO/ACL, declined UAC on the elevated task),
    // we revert the UI state so the toggle stays consistent with reality.
    [ObservableProperty]
    public partial bool AutostartEnabled { get; set; }

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (_isSyncing) return;
        bool ok = value ? StartupService.StartStartup() : StartupService.StopStartup();
        if (ok)
        {
            DeckleSettingsSource.Log.SettingChanged("Start with Windows", value.ToString());
            return;
        }

        // Write refused (GPO, ACL, missing ProcessPath, declined UAC…) — revert
        // the toggle so what the user sees matches what actually starts at logon.
        _isSyncing = true;
        try { AutostartEnabled = !value; }
        finally { _isSyncing = false; }
    }

    // ── Backup ───────────────────────────────────────────────────────────────
    //
    // BackupDirectory is the user override for where snapshots live (empty =
    // AppPaths.SettingsBackupDirectory, see SettingsService.ResolveBackupDirectory).
    // Backups is the live list refilled by RefreshBackups() — called on Load,
    // after CreateBackup, and any time BackupDirectory changes. The PowerToys-
    // style UI only surfaces the latest snapshot (file name + created at);
    // older snapshots remain on disk for manual access via the folder picker.

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
        DeckleSettingsSource.Log.SettingChanged("Paths.BackupDirectory", $"\"{value}\"");
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

        // Seed from the POCO initializers — the single source of truth for defaults,
        // shared with the composed manifest's reset selectors. No literal default
        // is spelled here; change a default in AppSettings and both the seed and the
        // per-card reset follow. Load() overwrites these with persisted values; this
        // only covers the gap before the first Load.
        var appearance = new AppearanceSettings();
        var paste = new PasteSettings();
        var overlay = new OverlaySettings();
        var paths = new PathsSettings();

        Theme = appearance.Theme;
        AutoPasteEnabled = paste.AutoPasteEnabled;
        OverlayEnabled = overlay.Enabled;
        OverlayFadeOnProximity = overlay.FadeOnProximity;
        OverlayAnimations = overlay.Animations;
        // Same normalization Load() applies, so the seeded position matches a picker
        // option even if the POCO default were ever a legacy corner value.
        OverlayPosition = (overlay.Position ?? "").StartsWith("Top") ? "TopCenter" : "BottomCenter";
        // Autostart is OS-backed (registry + scheduled task), not an AppSettings
        // POCO — its conceptual default lives on the startup facade.
        AutostartEnabled = StartupService.DefaultEnabled;
        BackupDirectory = paths.BackupDirectory;

        // _isSyncing stays true — Load() will set it to false.
    }

    public void Load()
    {
        _isSyncing = true;
        try
        {
            var shell = SettingsService.Instance.Current;
            Theme = shell.Appearance.Theme;
            AutoPasteEnabled = shell.Paste.AutoPasteEnabled;
            OverlayEnabled = shell.Overlay.Enabled;
            OverlayFadeOnProximity = shell.Overlay.FadeOnProximity;
            OverlayAnimations = shell.Overlay.Animations;
            // Normalize legacy corner values (TopLeft/BottomRight…) to the two
            // centre positions the picker now exposes, so the composed Choice
            // always matches a real option. Was done in the page's combo sync,
            // which the Group migration removed.
            OverlayPosition = (shell.Overlay.Position ?? "").StartsWith("Top") ? "TopCenter" : "BottomCenter";
            AutostartEnabled = StartupService.StartsAtLogon();
            BackupDirectory = shell.Paths.BackupDirectory;
        }
        finally
        {
            _isSyncing = false;
        }

        // Refresh outside the _isSyncing guard so any future logic in
        // RefreshBackups that touches observable state behaves normally.
        RefreshBackups();
    }

    private void PushToSettings()
    {
        var shell = SettingsService.Instance.Current;
        shell.Appearance.Theme = Theme;
        shell.Paste.AutoPasteEnabled = AutoPasteEnabled;
        shell.Overlay.Enabled = OverlayEnabled;
        shell.Overlay.FadeOnProximity = OverlayFadeOnProximity;
        shell.Overlay.Animations = OverlayAnimations;
        shell.Overlay.Position = OverlayPosition;
        shell.Paths.BackupDirectory = BackupDirectory ?? "";
        SettingsService.Instance.Save();
    }

    // Section resets moved out of the VM in the composer-reset slice: the composed
    // manifest carries each setting's default (read from the POCO), and the page's
    // section "Reset" links call the matching SettingsComposer.ResetAll(), which
    // drives every value back through its own setter. The old ResetXxxDefaults
    // methods were a second copy of the defaults and the seed above a third — both
    // now read new XxxSettings(), so the literal defaults live in one place.
}

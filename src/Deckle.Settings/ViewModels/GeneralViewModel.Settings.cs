using System;
using System.Collections.Generic;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Shell;

namespace Deckle.Settings;

// ── GeneralViewModel — settings manifest ──────────────────────────────────────
//
// The declarative half of the page, kept beside the ViewModel that owns the
// values rather than in the page code-behind. Each entry declares one setting —
// its kind, its localization key, its glyph, and typed selectors onto this VM's
// own properties — and SettingsComposer turns the list into SettingsCards.
//
// The Appearance and Startup sections are composed from here — the theme picker and
// the registry-backed autostart toggle (the Behaviour section's overlay + auto-paste
// moved to the Dictation page in the settings reorg). Each descriptor carries its reset default,
// read from the matching AppSettings POCO initializer so the literal defaults live
// in one place. GeneralPage's remaining controls stay hand-authored: the version row
// and the command/diagnostic cards under Application data. The
// composer drives only what it cleanly can.
public partial class GeneralViewModel
{
    // Appearance section — the theme picker. A Choice over the theme string the VM
    // already owns ("System"/"Light"/"Dark"), the values the ComboBoxItem Tags
    // carried; the option labels resolve from the page's .resw "<key>.Content"
    // entries. The change handler (OnThemeChanged → PushToSettings + ApplyTheme)
    // is unchanged: the composer drives the setter, the side effect rides along —
    // which is exactly why a card whose effect lives in the VM setter composes.
    public IReadOnlyList<SettingDescriptor> AppearanceSettings =>
    [
        Setting.Choice<string>("GeneralAppThemeCard",
            () => Theme,
            value => Theme = value,
            [
                ("System", "GeneralThemeSystem"),
                ("Light", "GeneralThemeLight"),
                ("Dark", "GeneralThemeDark"),
            ],
            glyph: Glyphs.Theme,
            // The default is the POCO initializer, the one source of truth — the VM
            // no longer carries its own copy. ("System".)
            defaultValue: () => new AppearanceSettings().Theme),
    ];

    // Hotkeys section — one card per hotkey action: Record, then one per module
    // switch of the tray panel, in the panel's order. Each card is hidden when
    // the App did not build its action (module absent). Record may not be
    // cleared, it is the dictation's keyboard entry point; a toggle may. The
    // defaults are the HotkeysSettings initializers (Record = Win + the key
    // left of 1, toggles unbound).
    public IReadOnlyList<SettingDescriptor> HotkeySettings =>
    [
        Hotkey("record", "GeneralHotkeyRecordCard", () => RecordHotkey, v => RecordHotkey = v,
            () => new HotkeysSettings().Record, allowUnbound: false),
        Hotkey("precision-scroll", "GeneralHotkeyPrecisionScrollCard", () => PrecisionScrollHotkey, v => PrecisionScrollHotkey = v,
            () => new HotkeysSettings().PrecisionScroll),
        Hotkey("trackpad", "GeneralHotkeyTrackpadCard", () => TrackpadHotkey, v => TrackpadHotkey = v,
            () => new HotkeysSettings().Trackpad),
        Hotkey("taskbar-cover", "GeneralHotkeyTaskbarCoverCard", () => TaskbarCoverHotkey, v => TaskbarCoverHotkey = v,
            () => new HotkeysSettings().TaskbarCover),
        Hotkey("autocorrect", "GeneralHotkeyAutocorrectCard", () => AutocorrectHotkey, v => AutocorrectHotkey = v,
            () => new HotkeysSettings().Autocorrect),
        Hotkey("rewrite", "GeneralHotkeyRewriteCard", () => RewriteHotkey, v => RewriteHotkey = v,
            () => new HotkeysSettings().Rewrite),
        Hotkey("ambient", "GeneralHotkeyAmbientLightCard", () => AmbientLightHotkey, v => AmbientLightHotkey = v,
            () => new HotkeysSettings().AmbientLight),
    ];

    // Every hotkey action with the card that edits it, for the duplicate check.
    private (string Id, string LabelKey, Func<HotkeyChord?> Get)[] HotkeyEntries =>
    [
        ("record", "GeneralHotkeyRecordCard", () => RecordHotkey),
        ("precision-scroll", "GeneralHotkeyPrecisionScrollCard", () => PrecisionScrollHotkey),
        ("trackpad", "GeneralHotkeyTrackpadCard", () => TrackpadHotkey),
        ("taskbar-cover", "GeneralHotkeyTaskbarCoverCard", () => TaskbarCoverHotkey),
        ("autocorrect", "GeneralHotkeyAutocorrectCard", () => AutocorrectHotkey),
        ("rewrite", "GeneralHotkeyRewriteCard", () => RewriteHotkey),
        ("ambient", "GeneralHotkeyAmbientLightCard", () => AmbientLightHotkey),
    ];

    private SettingDescriptor Hotkey(
        string id, string labelKey, Func<HotkeyChord?> get, Action<HotkeyChord?> set,
        Func<HotkeyChord?> defaultValue, bool allowUnbound = true)
        => Setting.Hotkey(labelKey, get, set,
            new HotkeyArgs(allowUnbound),
            glyph: Glyphs.Shortcut,
            visibleWhen: () => IsHotkeyActionAvailable(id),
            defaultValue: defaultValue,
            advisory: () => DuplicateHotkeyAdvisory(id, get()));

    private static bool IsHotkeyActionAvailable(string id)
        => SettingsHost.IsHotkeyActionAvailable?.Invoke(id) ?? true;

    // Two actions on one chord: Windows gives the chord to the first one
    // registered and refuses the second, so the card says which other action
    // already holds it. Hidden actions are skipped, their chord is not registered.
    private string? DuplicateHotkeyAdvisory(string id, HotkeyChord? chord)
    {
        if (chord is not { IsComplete: true } c) return null;

        foreach (var other in HotkeyEntries)
        {
            if (other.Id == id || !IsHotkeyActionAvailable(other.Id)) continue;
            if (other.Get() == c)
            {
                string header = Loc.GetFrom("Deckle.Settings", $"{other.LabelKey}/Header");
                return string.Format(Loc.GetFrom("Deckle.Settings", "GeneralHotkeyAlreadyUsed_Format"), header);
            }
        }
        return null;
    }

    // Startup section — "start with Windows". A plain TwoWay toggle, but its
    // OnAutostartEnabledChanged drives StartupService across both logon vehicles
    // and, when the write is refused (GPO/ACL, declined UAC), reverts the property
    // under the sync guard. That revert composes: the composer drives the setter,
    // and the reverted value rides back to the toggle on PropertyChanged — the
    // same path Load() and the section Reset use. The textbook "side effect in the
    // VM setter" card, migrated now that the theme picker proved that path carries
    // the effect through unchanged.
    public IReadOnlyList<SettingDescriptor> StartupSettings =>
    [
        Setting.Toggle("GeneralAutostartCard",
            () => AutostartEnabled,
            value => AutostartEnabled = value,
            glyph: Glyphs.Launch,
            // The OS is the source of truth; "no vehicle registered" is the default
            // (StartupService.DefaultEnabled, false). Resetting drives the toggle
            // off, whose setter calls StartupService.StopStartup() — the vehicle
            // removal rides the setter exactly like the theme/overlay side effects.
            defaultValue: () => StartupService.DefaultEnabled),
    ];

    // Updates section — the silent-check opt-out. A plain TwoWay toggle over the
    // AppSettings POCO; the check itself runs App-side (UpdateService), which
    // reads the persisted value on each tick, so flipping the toggle needs no
    // side effect here beyond PushToSettings.
    public IReadOnlyList<SettingDescriptor> UpdatesSettings =>
    [
        Setting.Toggle("GeneralUpdateAutoCheckCard",
            () => UpdateAutoCheckEnabled,
            value => UpdateAutoCheckEnabled = value,
            glyph: Glyphs.Refresh,
            defaultValue: () => new UpdatesSettings().AutoCheckEnabled),
    ];

    // Application data section — the backup-location folder picker. A Path
    // descriptor (FolderPickerMode.Configure: read-only readout with Change + Open)
    // over the BackupDirectory string the VM already owns; its OnBackupDirectoryChanged
    // (PushToSettings + RefreshBackups) rides the setter unchanged, exactly as the
    // theme/overlay side effects do. DefaultPath is the deferred AppPaths lookup the
    // code-behind used to push into the picker (the empty-value fallback, computed at
    // compose time), moved here into PathArgs so the manifest carries it. The reset
    // default is the POCO initializer (empty → "empty means AppPaths"), the one source
    // of truth. The Create/Restore actions and the latest-backup readout stay hand-
    // authored on the page (they are commands/readouts, not settable values).
    public IReadOnlyList<SettingDescriptor> ApplicationDataSettings =>
    [
        Setting.Path("GeneralBackupLocationCard",
            () => BackupDirectory,
            value => BackupDirectory = value,
            new PathArgs(
                FolderPickerMode.Configure,
                DefaultPath: () => AppPaths.SettingsBackupDirectory),
            glyph: Glyphs.Folder,
            defaultValue: () => new PathsSettings().BackupDirectory),
    ];
}

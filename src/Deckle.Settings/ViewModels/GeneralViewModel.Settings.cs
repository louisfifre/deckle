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
// own properties — and SettingsComposer turns the list into SettingsCards. Each
// reset default reads the matching AppSettings POCO initializer, so the literal
// defaults live in one place.
public partial class GeneralViewModel
{
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
            defaultValue: () => new AppearanceSettings().Theme),
    ];

    // Record, then one card per module switch of the tray panel, in the panel's
    // order. Record may not be cleared, it is the dictation's keyboard entry
    // point; a toggle may.
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

    // Side effects live in the VM setters, so the composer can drive them. The
    // autostart setter drives StartupService and, when the write is refused,
    // reverts the property under the sync guard; the reverted value rides back to
    // the toggle on PropertyChanged, the same path Load() and the section Reset use.
    public IReadOnlyList<SettingDescriptor> StartupSettings =>
    [
        Setting.Toggle("GeneralAutostartCard",
            () => AutostartEnabled,
            value => AutostartEnabled = value,
            defaultValue: () => StartupService.DefaultEnabled),
    ];

    // The App-side update loop reads the persisted value on each tick, so the
    // toggle needs no side effect beyond PushToSettings.
    public IReadOnlyList<SettingDescriptor> UpdatesSettings =>
    [
        Setting.Toggle("GeneralUpdateAutoCheckCard",
            () => UpdateAutoCheckEnabled,
            value => UpdateAutoCheckEnabled = value,
            defaultValue: () => new UpdatesSettings().AutoCheckEnabled),
    ];

    public IReadOnlyList<SettingDescriptor> ApplicationDataSettings =>
    [
        Setting.Path("GeneralBackupLocationCard",
            () => BackupDirectory,
            value => BackupDirectory = value,
            new PathArgs(
                FolderPickerMode.Configure,
                DefaultPath: () => AppPaths.SettingsBackupDirectory),
            defaultValue: () => new PathsSettings().BackupDirectory),
    ];
}

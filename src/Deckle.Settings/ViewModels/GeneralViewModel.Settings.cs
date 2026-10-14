using System;
using System.Collections.Generic;
using System.Linq;
using Deckle.Catalog;
using Deckle.Core;
using Deckle.Shell;

namespace Deckle.Settings;

// ── GeneralViewModel — settings manifest ──────────────────────────────────────
//
// The declarative half of the General and Shortcuts pages, kept beside the
// ViewModel that owns the values rather than in the page code-behind. Each entry
// declares one setting — its kind, its localization key, and typed selectors onto
// this VM's own properties — and SettingsComposer turns the list into
// SettingsCards. Each reset default reads the matching AppSettings POCO
// initializer, so the literal defaults live in one place.
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

    // ── Shortcuts ────────────────────────────────────────────────────────────
    //
    // One list per section of the Shortcuts page, grouped by what the action
    // drives. Record may not be cleared, it is the dictation's keyboard entry
    // point; a toggle may. The id lists mirror the descriptor lists so the page
    // can hide a section whose every action is absent.
    public static readonly IReadOnlyList<string> DictationHotkeyIds = ["record", "rewrite"];
    public static readonly IReadOnlyList<string> InputHotkeyIds = ["precision-scroll", "trackpad", "autocorrect"];
    public static readonly IReadOnlyList<string> DisplayHotkeyIds = ["taskbar-cover", "ambient"];

    public IReadOnlyList<SettingDescriptor> DictationHotkeys =>
    [
        Hotkey("record", "GeneralHotkeyRecordCard", () => RecordHotkey, v => RecordHotkey = v,
            () => new HotkeysSettings().Record, allowUnbound: false),
        Hotkey("rewrite", "GeneralHotkeyRewriteCard", () => RewriteHotkey, v => RewriteHotkey = v,
            () => new HotkeysSettings().Rewrite),
    ];

    public IReadOnlyList<SettingDescriptor> InputHotkeys =>
    [
        Hotkey("precision-scroll", "GeneralHotkeyPrecisionScrollCard", () => PrecisionScrollHotkey, v => PrecisionScrollHotkey = v,
            () => new HotkeysSettings().PrecisionScroll),
        Hotkey("trackpad", "GeneralHotkeyTrackpadCard", () => TrackpadHotkey, v => TrackpadHotkey = v,
            () => new HotkeysSettings().Trackpad),
        Hotkey("autocorrect", "GeneralHotkeyAutocorrectCard", () => AutocorrectHotkey, v => AutocorrectHotkey = v,
            () => new HotkeysSettings().Autocorrect),
    ];

    public IReadOnlyList<SettingDescriptor> DisplayHotkeys =>
    [
        Hotkey("taskbar-cover", "GeneralHotkeyTaskbarCoverCard", () => TaskbarCoverHotkey, v => TaskbarCoverHotkey = v,
            () => new HotkeysSettings().TaskbarCover),
        Hotkey("ambient", "GeneralHotkeyAmbientLightCard", () => AmbientLightHotkey, v => AmbientLightHotkey = v,
            () => new HotkeysSettings().AmbientLight),
    ];

    // A section title over no card reads as a broken page, so the page hides the
    // section whole when none of its actions was built.
    public static bool AnyHotkeyAvailable(IReadOnlyList<string> ids)
        => ids.Any(IsHotkeyActionAvailable);

    // Every hotkey action with the card that edits it, for the duplicate check.
    // The check spans the three sections: a chord is global, whatever section
    // its card sits in.
    private (string Id, string LabelKey, Func<HotkeyChord?> Get)[] HotkeyEntries =>
    [
        ("record", "GeneralHotkeyRecordCard", () => RecordHotkey),
        ("rewrite", "GeneralHotkeyRewriteCard", () => RewriteHotkey),
        ("precision-scroll", "GeneralHotkeyPrecisionScrollCard", () => PrecisionScrollHotkey),
        ("trackpad", "GeneralHotkeyTrackpadCard", () => TrackpadHotkey),
        ("autocorrect", "GeneralHotkeyAutocorrectCard", () => AutocorrectHotkey),
        ("taskbar-cover", "GeneralHotkeyTaskbarCoverCard", () => TaskbarCoverHotkey),
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

    // The backup folder, composed as the child of the page's hand-authored
    // "Settings backup" fold (Compose inFold), so it renders as the Location
    // line alone; the fold carries the title and the sentence.
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

    // The data folder, standing alone, so the composer renders it as the folder
    // fold: "Data folder" and its sentence in the header, the Location line
    // inside. Choosing a folder is the relocation (DataFolderMoveRequested), so
    // the mode follows the App's relocate hook: unwired, the line offers Open
    // alone, as the Move button used to hide. No defaultValue: there is no
    // default root to go back to, a reset would be another move.
    public IReadOnlyList<SettingDescriptor> DataFolderSettings =>
    [
        Setting.Path("GeneralDataFolderCard",
            () => DataFolder,
            RequestDataFolderMove,
            new PathArgs(
                SettingsHost.RelocateDataRoot is null
                    ? FolderPickerMode.OpenOnly
                    : FolderPickerMode.Configure,
                DefaultPath: () => AppPaths.UserDataRoot),
            enabledWhen: () => !DataFolderMoveInFlight),
    ];
}

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
// The Appearance, Behaviour and Startup sections are composed from here — theme
// picker, overlay group (master + fade/animations/position), auto-paste, and the
// registry-backed autostart toggle. Each descriptor also carries its reset default,
// read from the matching AppSettings POCO initializer so the literal defaults live
// in one place. GeneralPage's remaining controls stay hand-authored: the read-only
// shortcut readouts and the command/diagnostic cards under Application data. The
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

    // Behaviour section — the overlay group, then the flat auto-paste toggle. The
    // overlay is the first Group descriptor: a master toggle (OverlayEnabled) that
    // reveals three children — fade-on-proximity and animations toggles, and the
    // position Choice — each HIDDEN by the composer while the master is off. The
    // hand-authored SettingsExpander greyed them (IsEnabled); the composer masks
    // them instead, the Microsoft-first dependency gating. The position Choice
    // matches the canonical "TopCenter"/"BottomCenter" values the VM normalizes on
    // Load (legacy corner values folded to a centre), so a persisted value always
    // selects a real option. Every change handler (OnOverlay*Changed →
    // PushToSettings) is unchanged; the composer only drives the UI.
    public IReadOnlyList<SettingDescriptor> BehaviourSettings =>
    [
        Setting.Group("GeneralOverlayExpander",
            () => OverlayEnabled,
            value => OverlayEnabled = value,
            [
                Setting.Toggle("GeneralOverlayFadeCard",
                    () => OverlayFadeOnProximity,
                    value => OverlayFadeOnProximity = value,
                    defaultValue: () => new OverlaySettings().FadeOnProximity),
                Setting.Toggle("GeneralOverlayAnimationsCard",
                    () => OverlayAnimations,
                    value => OverlayAnimations = value,
                    defaultValue: () => new OverlaySettings().Animations),
                Setting.Choice<string>("GeneralOverlayPositionCard",
                    () => OverlayPosition,
                    value => OverlayPosition = value,
                    [
                        ("TopCenter", "GeneralOverlayPositionTop"),
                        ("BottomCenter", "GeneralOverlayPositionBottom"),
                    ],
                    // The descriptor value is the NORMALIZED position string the
                    // picker exposes; the POCO default may be a legacy corner value,
                    // so fold it through the same Top→TopCenter / else→BottomCenter
                    // rule Load() applies, or the reset would target a non-option.
                    defaultValue: () =>
                        (new OverlaySettings().Position ?? "").StartsWith("Top")
                            ? "TopCenter"
                            : "BottomCenter"),
            ],
            glyph: Glyphs.Overlay,
            // The master's default is the overlay POCO's Enabled initializer (true).
            defaultValue: () => new OverlaySettings().Enabled),

        Setting.Toggle("GeneralAutoPasteCard",
            () => AutoPasteEnabled,
            value => AutoPasteEnabled = value,
            glyph: Glyphs.Paste,
            defaultValue: () => new PasteSettings().AutoPasteEnabled),
    ];

    // Startup section — "start with Windows". A plain TwoWay toggle, but its
    // OnAutostartEnabledChanged writes HKCU and, when the write is refused
    // (GPO/ACL), reverts the property under the sync guard. That revert composes:
    // the composer drives the setter, and the reverted value rides back to the
    // toggle on PropertyChanged — the same path Load() and the section Reset use.
    // The textbook "side effect in the VM setter" card, migrated now that the
    // theme picker proved that path carries the effect through unchanged.
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

    public IReadOnlyList<SettingDescriptor> StartupSettings =>
    [
        Setting.Toggle("GeneralAutostartCard",
            () => AutostartEnabled,
            value => AutostartEnabled = value,
            glyph: Glyphs.Launch,
            // The registry is the source of truth; "not registered" is the default
            // (AutostartService.DefaultEnabled, false). Resetting drives the toggle
            // off, whose setter calls AutostartService.Disable() — the registry
            // write rides the setter exactly like the theme/overlay side effects.
            defaultValue: () => AutostartService.DefaultEnabled),
    ];
}

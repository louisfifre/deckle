using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// The shell pages' search contribution: one SettingSearchEntry per card a page wants
// findable, handed to the shell index at boot alongside the module pages' entries.
// General and Shortcuts are static shell anchors, not registry modules, so they carry
// no SettingsModuleDescriptor the index could read a manifest off — these lists ARE
// their search manifests, Entries for GeneralPage and HotkeyEntries for HotkeysPage.
// The bespoke cards (the backup fold with its commands, the latest-backup readout,
// the setup wizard) have no manifest at all, so a hand-written entry is their only
// possible declaration; the composed cards are declared here under the same contract.
//
// Maintenance contract: a card added to GeneralPage.xaml or HotkeysPage that should be
// findable is one entry in its page's list, its LabelKey matching the card's x:Uid
// (bespoke) or the descriptor's LabelKey (composed) — the same string stamped on the
// card's Tag, the handle the post-navigation scroll-to walks the tree for. Keywords
// are English lowercase synonyms a user might type instead of the visible label; they
// widen matching only, never shown. No LiteralLabel is set: each card's
// "<LabelKey>/Header" entry in this module's .resw resolves the text.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // ── Appearance (composed: GeneralViewModel.AppearanceSettings) ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralAppThemeCard",
            Keywords = ["appearance", "dark mode", "color"],
        },

        // ── Startup (composed: GeneralViewModel.StartupSettings) ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralAutostartCard",
            Keywords = ["boot", "startup", "autostart", "login"],
        },

        // ── Application data, in the page's order ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralBackupExpander",
            Keywords = ["export", "import", "save"],
        },
        // Composed: GeneralViewModel.ApplicationDataSettings, the Location line
        // inside the expander; its /Header entry names it for the search alone.
        new SettingSearchEntry
        {
            LabelKey = "GeneralBackupLocationCard",
            Keywords = ["backup", "folder", "path", "cloud"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralBackupInfoCard",
            Keywords = ["snapshot", "restore"],
        },
        // Composed: GeneralViewModel.DataFolderSettings, the folder fold.
        new SettingSearchEntry
        {
            LabelKey = "GeneralDataFolderCard",
            Keywords = ["directory", "path", "storage", "move"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralRerunSetupCard",
            Keywords = ["onboarding", "reconfigure", "reinstall"],
        },
    ];

    // In the page's order: Dictation, Input, Display and lights.
    public static IReadOnlyList<SettingSearchEntry> HotkeyEntries { get; } =
    [
        // ── Dictation (composed: GeneralViewModel.DictationHotkeys) ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyRecordCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "dictation", "transcription"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyRewriteCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "rewrite"],
        },

        // ── Input (composed: GeneralViewModel.InputHotkeys) ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyPrecisionScrollCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "precision scrolling"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyTrackpadCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "trackpad"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyAutocorrectCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "autocorrect"],
        },

        // ── Display and lights (composed: GeneralViewModel.DisplayHotkeys) ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyTaskbarCoverCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "taskbar cover"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyAmbientLightCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "ambient light"],
        },
    ];
}

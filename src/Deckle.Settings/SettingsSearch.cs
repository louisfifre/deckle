using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// GeneralPage's search contribution: one SettingSearchEntry per card the page wants
// findable, handed to the shell index at boot alongside the module pages' entries.
// General is a static shell anchor, not a registry module, so it carries no
// SettingsModuleDescriptor the index could read a manifest off — this list IS its
// search manifest. The bespoke cards (the data-folder and backup commands) have no
// manifest at all, so a hand-written entry is their only possible declaration; the
// composed cards are declared here under the same contract.
//
// Maintenance contract: a card added to GeneralPage.xaml that should be findable is
// one entry here, its LabelKey matching the card's x:Uid (bespoke) or the descriptor's
// LabelKey (composed) — the same string stamped on the card's Tag, the handle the
// post-navigation scroll-to walks the tree for. Keywords are English lowercase
// synonyms a user might type instead of the visible label; they widen matching only,
// never shown. No LiteralLabel is set: each card's "<LabelKey>/Header" entry in this
// module's .resw resolves the text.
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

        // ── Hotkeys (composed: GeneralViewModel.HotkeySettings) ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyRecordCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "dictation", "transcription"],
        },
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
            LabelKey = "GeneralHotkeyTaskbarCoverCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "taskbar cover"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyAutocorrectCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "autocorrect"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyRewriteCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "rewrite"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralHotkeyAmbientLightCard",
            Keywords = ["shortcut", "hotkey", "keybinding", "ambient light"],
        },

        // ── Startup (composed: GeneralViewModel.StartupSettings) ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralAutostartCard",
            Keywords = ["boot", "startup", "autostart", "login"],
        },

        // ── Application data ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralRerunSetupCard",
            Keywords = ["onboarding", "reconfigure", "reinstall"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralDataFolderCard",
            Keywords = ["directory", "path", "storage"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralBackupExpander",
            Keywords = ["export", "import", "save"],
        },
        // Composed: GeneralViewModel.ApplicationDataSettings, hosted inside the expander.
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
    ];
}

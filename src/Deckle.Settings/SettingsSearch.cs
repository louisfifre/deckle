using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Settings;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// GeneralPage's search contribution: one SettingSearchEntry per card the page wants
// findable, handed to the shell index at boot alongside the module pages' entries.
// General is a static shell anchor, not a registry module, so it carries no
// SettingsModuleDescriptor the index could read a manifest off — this list IS its
// search manifest.
//
// Why the entries are restated here rather than derived from the page. The composed
// cards (theme, autostart, backup location) live as instance properties on
// GeneralViewModel — AppearanceSettings, StartupSettings, ApplicationDataSettings —
// so reaching their descriptors means constructing the VM, which pulls registry and
// OS reads through its side-effecting setters. The index may not do that at boot to
// enumerate cards, so the LabelKeys are declared flat and side-effect-free. The
// bespoke cards (the shortcut readouts, the data-folder and backup commands) have no
// manifest at all — they only ever existed as XAML — so a hand-written entry is their
// only possible declaration. Every General card thus lives here, composed and bespoke
// alike, under one contract.
//
// Maintenance contract: a card added to GeneralPage.xaml that should be findable is
// one entry here, its LabelKey matching the card's x:Uid (bespoke) or the descriptor's
// LabelKey (composed) — the same string stamped on the card's Tag, the handle the
// post-navigation scroll-to walks the tree for. Keywords are English lowercase
// synonyms a user might type instead of the visible label; they widen matching only,
// never shown. No LiteralLabel is set: every General card has a "<LabelKey>/Header"
// entry in this module's .resw, so the index resolves the text itself.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // ── Shortcuts ──
        new SettingSearchEntry
        {
            LabelKey = "GeneralTranscribeCard",
            Keywords = ["shortcut", "dictation", "voice"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralPrimaryRewriteCard",
            Keywords = ["shortcut", "hotkey", "llm"],
        },
        new SettingSearchEntry
        {
            LabelKey = "GeneralSecondaryRewriteCard",
            Keywords = ["shortcut", "hotkey", "llm"],
        },

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

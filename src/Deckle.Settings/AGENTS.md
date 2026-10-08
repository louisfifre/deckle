---
description: Settings shell — hosts module-owned pages and non-modular settings.
type: agent-instructions
---

# AGENTS.md — Deckle.Settings

- A domain's page lives in its domain module and is resolved by `Type.GetType(tag)`. The shell owns only the non-modular settings. What to expose and how: the `deckle-settings-ux` skill.
- `SettingsHost` is one nominal delegate field per shell capability, not a service locator. Callers stay null-safe for when the shell has not wired it.
- Search contract for a module page: one `SettingSearchEntry` per findable card, `Tag="<key>"` in XAML on bespoke cards. Folded children stay unindexed, because a hit must be brought into view; the fold card's keywords bridge to them.
- `SettingsBootstrap.MigrateLegacyToPerModule()` runs first in `OnLaunched`. Otherwise a service writes its defaults and the migration finds the target already populated.
- Use the Windows App SDK `Microsoft.Windows.Storage.Pickers.FolderPicker`, never the UWP one: the UWP picker does not work under elevation.

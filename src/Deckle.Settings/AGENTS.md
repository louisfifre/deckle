---
description: Settings shell — aggregates module-owned pages in a NavigationView, owns non-modular persistence and the SettingsHost delegate registry.
type: agent-instructions
---

# AGENTS.md — Deckle.Settings

The Settings UI shell. It hosts the window (adaptive NavigationView + page Frame) and **aggregates** pages — it does not own domain doctrine: the page that configures a domain lives in that domain's module (`WhisperPage` in `Deckle.Transcription`, `AmbientPage` in `Deckle.Lighting.Ambient`, …), resolved dynamically via `Type.GetType(tag)` from the nav item's `Tag`. The shell owns only the non-modular settings (`SettingsService`) and the cross-cutting plumbing below. What to expose, how to group and surface it lives in the `deckle-settings-ux` skill, not here.

## SettingsHost — shell-side delegate registry

A static set of typed delegates the app wires at boot (`ApplyTheme`, `RestartApp`, `GetSettingsWindow`, `OpenSetupWizard`, …). A module page calls them (`SettingsHost.X?.Invoke(...)`) to trigger a shell action without `Deckle.Settings` ever referencing the host project. Deliberately not a disguised service locator: each capability is one nominal field, added and wired explicitly, null-safe when the shell hasn't wired it (isolated module test).

## Cross-page search index

`SettingsSearchIndex` (static, filled by the composition root at boot, same shape as `SettingsModuleRegistry`) lets the TitleBar box reach a card on any page without composing it: text resolves from each module's PRI subtree, keyed by the card's `LabelKey` — which the composer also stamps as the card's `Tag`, the scroll-to handle. Contribution contract for a module page: one `SettingSearchEntry` per findable card in the module's `SettingsSearch.cs`, plus `Tag="<key>"` in XAML on bespoke cards; folded children stay unindexed (a hit must be bringable into view) and bridge through the fold card's keywords.

## Per-module persistence

Each module owns its `modules/<id>/settings.json`; the shell keeps only `settings.json`. Invariant: `SettingsBootstrap.MigrateLegacyToPerModule()` runs first in `App.OnLaunched`, before any service touches its file — otherwise the service writes defaults and the migration sees an already-populated target. New module migrations follow the same method.

## FolderPicker — elevation-safe API

Folder pickers use `Microsoft.Windows.Storage.Pickers.FolderPicker(WindowId)` (the Windows App SDK API), never the legacy UWP `Windows.Storage.Pickers.FolderPicker` — that one needs `WinRT.Interop.InitializeWithWindow` in a desktop app and, more importantly, doesn't work under elevation (the new API exists precisely to close that gap). The `WindowId` comes from `SettingsHost.GetSettingsWindow()`.

---
description: Settings shell — aggregates module-owned pages in a NavigationView, owns non-modular persistence and the SettingsHost delegate registry.
type: agent-instructions
---

# AGENTS.md — Deckle.Settings

The Settings UI shell. It hosts the window (adaptive NavigationView + page Frame) and **aggregates** pages — it does not own domain doctrine: the page that configures a domain lives in that domain's module (`WhisperPage` in `Deckle.Transcription`, `AmbientPage` in `Deckle.Lighting.Ambient`, …), resolved dynamically via `Type.GetType(tag)` from the nav item's `Tag`. The shell owns only the non-modular settings (`SettingsService`) and the cross-cutting plumbing below. What to expose, how to group and surface it lives in the `deckle-settings-ux` skill, not here.

## SettingsHost — shell-side delegate registry

A static set of typed delegates the app wires at boot (`ApplyTheme`, `RestartApp`, `GetSettingsWindow`, `OpenSetupWizard`, …). A module page calls them (`SettingsHost.X?.Invoke(...)`) to trigger a shell action without `Deckle.Settings` ever referencing the host project. Deliberately not a disguised service locator: each capability is one nominal field, added and wired explicitly, null-safe when the shell hasn't wired it (isolated module test).

## Per-module persistence

Each module owns its `modules/<id>/settings.json`; the shell keeps only `settings.json`. Invariant: `SettingsBootstrap.MigrateLegacyToPerModule()` runs first in `App.OnLaunched`, before any service touches its file — otherwise the service writes defaults and the migration sees an already-populated target. New module migrations follow the same method.

## FolderPicker — elevation-safe API

Folder pickers use `Microsoft.Windows.Storage.Pickers.FolderPicker(WindowId)` (the Windows App SDK API), never the legacy UWP `Windows.Storage.Pickers.FolderPicker` — that one needs `WinRT.Interop.InitializeWithWindow` in a desktop app and, more importantly, doesn't work under elevation (the new API exists precisely to close that gap). The `WindowId` comes from `SettingsHost.GetSettingsWindow()`.

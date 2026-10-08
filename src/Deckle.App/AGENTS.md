---
description: WinUI 3 host — composition root, OnLaunched ordering, WinUI 3 pitfalls.
type: agent-instructions
---

# AGENTS.md — Deckle.App (host)

This is the composition root for the `Deckle.*` modules, and nothing more. Only event handlers and bridge adapters live here. Business logic that creeps in belongs in a module.

## OnLaunched

The startup order in `OnLaunched` compiles in any order but breaks silently when reordered. Keep these orderings:

- Settings migration runs before any service opens its file.
- Telemetry gates are set before the first write.
- The `MessageOnlyHost` exists before `RegisterHotKey`.
- The tray callbacks are wired before `tray.Register`.

The `App` constructor installs three unhandled-exception nets: `Application`, `AppDomain` and `TaskScheduler`. Without them, an exception from a background `TranscriptionEngine` handler vanishes silently.

## Silent WinUI 3 pitfalls

These give no compiler error and no visible symptom. They recur on any new XAML surface.

- UI-thread affinity: creating or touching a WinUI 3 UI object off the UI thread, a `SolidColorBrush` included, throws `COMException` (`RPC_E_WRONG_THREAD`). Build brushes and UI objects in the `Window` constructor and reuse them in handlers that arrive from the Record and Transcribe threads.
- `ItemsRepeater` does not propagate `DataContext` to the content its `DataTemplate` realizes, unlike `ListView`. A control in the template sees `DataContext == null` even when `x:Bind` resolves. Capture the view model with `Tag="{x:Bind}"` and read it back in the handler. Do not walk the visual tree for a parent `DataContext`: it breaks on the first template refactor.

---
description: WinUI 3 host composing the Deckle.* modules — the composition boundary, the OnLaunched ordering invariants, and the silent WinUI 3 pitfalls that each cost a session.
type: agent-instructions
---

# CLAUDE.md — Deckle.App (host)

Composition root for the `Deckle.*` modules — and nothing more. No business logic lives here beyond event handlers and bridge adapters; when one creeps in, it almost always belongs in a specific module instead.

## OnLaunched

The startup sequence in `App.xaml.cs` is load-bearing: the steps compile in any order but break silently if reordered. Settings migration must run before any service opens its file; telemetry gates before the first write; the `MessageOnlyHost` before `RegisterHotKey`; the tray's callbacks before `tray.Register`.

Three exception safety nets in the `App` constructor — the `Application` / `AppDomain` / `TaskScheduler` unhandled handlers, routed through `DeckleAppSource`. Without them an exception from a background `TranscriptionEngine` handler vanishes silently.

## Silent WinUI 3 pitfalls

No compiler error, no obvious symptom — nothing points at the cause, so each costs a session. They recur on any new XAML surface, not only here.

- **UI-thread affinity**: WinUI 3 UI objects carry thread affinity — creating or touching one (a `SolidColorBrush` included) off the UI thread throws `COMException` (`RPC_E_WRONG_THREAD`). Build brushes and UI objects in the `Window` constructor and reuse them in handlers arriving from Record/Transcribe threads.
- **`ItemsRepeater` doesn't propagate `DataContext`** to the content its `DataTemplate` realises, unlike `ListView`. A control nested in the template sees `DataContext == null` even when `x:Bind` resolves against the implicit item. Capture the VM with `Tag="{x:Bind}"` and read it back in the handler; don't walk the visual tree for a parent `DataContext` — it breaks on the first template refactor.

---
name: claude-deckle-shell
description: "Doctrine for Deckle.Shell, the system shell module (message-only host, tray, global hotkeys, autostart). Read before touching any of the four shell primitives or wiring them from the host app."
type: agent-instructions
module: Deckle.Shell
---

# CLAUDE.md — Deckle.Shell

System shell module. Covers interactions with the operating system that don't belong to any business pipeline: Win32 global hotkeys, tray icon, HKCU autostart, an invisible message-only window serving as the attachment point for the tray and the hotkeys, and a `DispatcherQueueExtensions` wrapper that signals rejected UI enqueues. The module is intentionally low-level: no application knowledge beyond these four primitives. The concrete actions behind each tray menu entry or each hotkey are wired by the host app before `Register` — no auto-binding, no service locator, no coupling to business modules.

## Message-only host

The tray and the global hotkeys cannot be hosted by a `Microsoft.UI.Xaml.Window`: the required Win32 subclassing (`SetWindowSubclass`) is incompatible. The canonical solution is a Win32 message-only window (`MessageOnlyHost`, parent `HWND_MESSAGE`) created in `App.OnLaunched`. Invisible by construction — no flash possible, no off-screen trick. `TrayIconManager.Register(hwnd)` and `HotkeyManager` attach to it. The wiring order matters: create the `MessageOnlyHost` before attempting `RegisterHotKey`, and wire the tray callbacks before `TrayIconManager.Register`.

Recurring technical pitfall: the Win32 `SubclassProc` delegate MUST be an instance field, never a local lambda. Otherwise the GC collects it and the subclass crashes when Windows tries to invoke it. The pattern is in place in `MessageOnlyHost`.

## Hotkeys

Three default global hotkeys: `Win+\`` (transcription), `Shift+Win+\`` (rewrite primary), `Ctrl+Win+\`` (rewrite secondary). All registered via Win32 `RegisterHotKey` on the `MessageOnlyHost`. Before any runtime test, kill any already-running instance — two processes that call `RegisterHotKey` on the same combination collide with `err 1409`.

The `HotkeyManager` also listens for keyboard layout changes (`WM_INPUTLANGCHANGE`) and re-resolves the VKs from the scancodes to preserve the combination on another layout. If the re-register fails (rare), it falls back to `Warning` without blocking — the user keeps a functional UI even if the hotkey momentarily drops.

## Tray

`TrayIconManager` registers a Shell_NotifyIcon icon with a context menu. The callbacks (start recording, open settings, open logs, quit) are supplied by the host app before `Register`. The icon can toggle between idle state and recording state (red) via `SetState` — it's the transcription pipeline that pushes the state at session start and end.

`TrayIconManager.GetIconRect()` expose le rect en pixels physiques (screen coordinates) de l'icône dans la zone de notification, via l'API `Shell_NotifyIconGetRect`. Retourne `null` si l'icône n'a pas pu être localisée (encore non enregistrée, dans l'overflow caché, ou explorer.exe en cours de restart). Consommé par `TrayContextMenuHost` pour ancrer son popup tangent à l'icône — pattern canonique Windows qui rend la position correcte quelle que soit l'orientation de la taskbar.

## Autostart

`AutostartService` manages the HKCU entry `Software\Microsoft\Windows\CurrentVersion\Run`. The written value targets `Environment.ProcessPath` (absolute path of the current exe). `Disable` does not touch an entry that points to another install — useful when the user has launched Deckle from a dev build while a release is installed elsewhere. States and errors are reported under `Lifecycle`. No MSIX StartupTask — decision recorded in [ADR-0002](../../docs/adr/0002-reporter-msix-rester-unpackaged.md).

## DispatcherQueueExtensions

`TryEnqueue` wrapper that logs at `Warning` when the UI dispatch is rejected (queue shut down). The caller passes a free-form source label (`"HUD"`, `"LOGWIN"`, etc.) that is prefixed in the ETW message — the structured payload keeps only the `what` (description of the lost event). Useful for spotting windows that try to marshal after their closure.

## Observability

All emissions go through `DeckleShellSource.Log` — provider `Deckle.Shell` (ETW name) exposed as a static singleton. The doctrine "observation attaches to the module that contains the operation" converges several legacy sources (`LogSource.Hotkey`, `LogSource.MsgHost`, `LogSource.Settings` for the autostart branch, plus the free-form `source` parameter of `DispatcherQueueExtensions`) onto a single provider — SHELL tag in the LogWindow. Keywords distinguish the internal sub-domains (`Lifecycle` for host/autostart, `Capture` for the hotkeys).

## Pointers

- [src/Deckle.App/CLAUDE.md](../Deckle.App/CLAUDE.md) — host app lifetime, wiring order (`MessageOnlyHost` before `RegisterHotKey`, tray callbacks before `TrayIconManager.Register`).
- [src/Deckle.Transcription/CLAUDE.md](../Deckle.Transcription/CLAUDE.md) — pipeline that consumes the transcription hotkey and that carries the paste doctrine (paste is not a shell primitive: it's a business policy of the transcription).
- [src/Deckle.Core/Interop/UIAutomation.cs](../Deckle.Core/Interop/UIAutomation.cs) — `IUIAutomation` wrapper consumed by the transcription for the focus probe.

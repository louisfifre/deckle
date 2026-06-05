---
description: System shell module — the low-level OS primitives (hotkeys, tray, autostart, message-only host) and the boundary that keeps them business-free.
type: agent-instructions
---

# CLAUDE.md — Deckle.Shell

OS interactions that belong to no business pipeline: global hotkeys, tray icon, HKCU autostart, and the invisible message-only window they attach to. The boundary is the point of the module — it holds no application knowledge beyond these primitives and binds nothing on its own. The host app supplies every concrete action (tray callback, hotkey handler) and wires them before `Register`; the shell never reaches into a business module.

The tray icon's idle ↔ recording state is pushed in by the transcription pipeline via `SetState`, not decided here — the shell doesn't know what recording means.

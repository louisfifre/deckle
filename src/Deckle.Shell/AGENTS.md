---
description: Low-level OS primitives (hotkeys, tray, autostart) kept business-free.
type: agent-instructions
---

# AGENTS.md — Deckle.Shell

- The shell holds no application knowledge and never reaches into a business module. The host app supplies every concrete action (tray callback, hotkey handler) and wires it before `Register`.
- The host app pushes the tray icon's idle or recording state through `UpdateStatus`. The shell never infers it from the localized status text: it does not know what recording means.
- Deckle.Shell stays XAML-free. Code that needs the XAML visual tree lives in a sibling module.

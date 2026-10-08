---
description: HUD overlays — non-focusable, click-through, always-on-top windows.
type: agent-instructions
---

# AGENTS.md — Deckle.Hud

A HUD shows information without ever stealing focus or interrupting the user.

## The no-focus invariant

- Show a HUD with `ShowWindow(SW_SHOWNOACTIVATE)`, then `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`. Reassert topmost on every visible transition. Never call `SetForegroundWindow`.
- `WS_EX_TRANSPARENT` makes it click-through; `WS_EX_LAYERED` carries its alpha.
- Engine events arrive on background threads: UI handlers marshal through the `DispatcherQueue`.
- The window is created once at boot and never destroyed.

## Pitfall

A `Foreground` set from code does not follow a `ThemeResource`. The brush is a local value, so on a light/dark switch it keeps the old theme's color until reassigned by hand. Declare it with `{ThemeResource}` in XAML wherever possible.

---
description: HUD overlay surface — non-focusable, click-through, always-on-top windows that show information without stealing focus.
type: agent-instructions
---

# AGENTS.md — Deckle.Hud

A HUD here is an on-screen overlay you can't click and can't focus: always-on-top, click-through, showing information without ever stealing focus or interrupting the user. The module owns that *kind of surface* — today its only instance is the transcription feedback (chrono, state, transient messages), but as Deckle grows other tools the surface is meant to generalize. Heavy visuals (animated strokes, shadows) are rendered through `Deckle.Composition`.

## The no-focus invariant

Everything else follows from this. A HUD shows via `ShowWindow(SW_SHOWNOACTIVATE)` then `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` — topmost reasserted on every visible transition, **never** `SetForegroundWindow`. `WS_EX_TRANSPARENT` makes it click-through, `WS_EX_LAYERED` carries its alpha. Engine events arrive on background threads, so UI handlers marshal through `DispatcherQueue.TryEnqueue`. The window is created once at boot and never destroyed.

## Motion-policy boundary

The HUD owns the runtime interpretation of motion preferences while it is their only domain consumer. Windows' animation preference governs simple visual transitions; `Overlay.Animations` independently governs functional HUD feedback. Keep the shared observer and gates here, never in `Deckle.Catalog`: the catalog is declarative vocabulary, not a runtime service host. Extract platform-level motion support only when a second independent domain needs the same observation contract; each animator still owns how it reaches a stable state when motion turns off.

## Pitfall

A `Foreground` set from code does not follow a `ThemeResource`: a code-assigned brush is a local value with no expression to re-evaluate, so on a light/dark switch (`ActualThemeChanged`) it keeps the old theme's color and must be reassigned by hand. Keep the value declarative with `{ThemeResource}` in XAML wherever possible.

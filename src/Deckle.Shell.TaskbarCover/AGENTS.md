---
description: Taskbar cover module — an opaque topmost band over the taskbar that fades as the cursor approaches it and re-covers the instant the cursor leaves, cursor signal via WinEvent hook (not Raw Input — contended).
type: agent-instructions
---

# AGENTS.md — Deckle.Shell.TaskbarCover

Masks the taskbar behind a black band: a layered, topmost tool window sized to the exact `ABM_GETTASKBARPOS` rect, whatever the anchored edge, rebuilt on `WM_SETTINGCHANGE`, `WM_DISPLAYCHANGE` and `TaskbarCreated`. A sibling of [Deckle.Shell](../Deckle.Shell/AGENTS.md) like TrayMenu: a domain module with its own runtime state, not a shell primitive.

## The cursor signal — why a WinEvent hook

`CursorMovementSignal` (Deckle.Shell) owns the process's only `RIDEV_INPUTSINK` Raw Input mouse registration; a second one would steal it. The cover hooks `EVENT_OBJECT_LOCATIONCHANGE` globally and keeps only `OBJID_CURSOR`: additive, asynchronous (no input-chain latency, unlike `WH_MOUSE_LL`), delivered on the hooking thread's pump. The callback runs at input cadence, so it stays trivial; `UnhookWinEvent` runs on the hooking thread; the delegate stays rooted. `WINEVENT_OUTOFCONTEXT` alone, without `SKIPOWNPROCESS`, so moves over Deckle's own windows are not lost.

## Threading

One dedicated thread owns the band window, the cursor hook and every state field, without a lock. `Start()` waits only on local calls; the first `SHAppBarMessage`, a synchronous send to Explorer, runs on the worker afterwards. A worker that outlives `Stop()`'s join is kept as defunct and `Start()` refuses to run until it has exited. The band is a real top-level window because the broadcasts it lives on never reach message-only windows. It never takes focus and is not click-through: a click on the band is swallowed.

## State machine

`UpdateCover` is the sole `ShowWindow` site: visible = layout known, not suppressed, not suspended, alpha > 0. The approach zone reaches `ApproachZoneFactor` × the band thickness from the anchored edge, band included. Inside it the alpha is a smoothstep of the cursor's distance to the band: 0 on the band (window hidden, the taskbar takes clicks), 255 at the zone boundary and beyond. Leaving the zone re-covers at once. Event driven only: the cursor hook drives the alpha, and boot, layout change and resume re-read it from the cursor. The factor and the curve are constants in `CoverGeometry`. The only setting is the master switch, `TaskbarCoverSettings.Enabled`: its Settings page (`TaskbarCoverPage`, composed from `TaskbarCoverViewModel`), the tray panel switch and the hotkey all write that flag, and the App's settings observer starts or stops the host.

Fullscreen suppression comes from `ForegroundWatcher` (Deckle.Shell.Foreground): its `Changed` is posted to the cover thread, which reads `Current`. `CoverSuppression.Applies` suppresses only when the fullscreen window is on the band's monitor (`MonitorFromRect` of the band, re-read on every layout query). Sleep and session lock park the machine.

Z-order: the taskbar is topmost too and the last positioned topmost window wins. While visible, the band re-asserts `HWND_TOPMOST` on every foreground change, on return to full opacity, and on every layout message, even when the rect is unchanged.

## Failure posture

No fabricated geometry: when `ABM_GETTASKBARPOS` fails the band stays hidden until a later layout message finds the taskbar. A dead cursor hook fails the start, since the band would never reveal the taskbar. A watcher that failed to start is logged by the watcher; its snapshot stays empty, so the band is never suppressed.

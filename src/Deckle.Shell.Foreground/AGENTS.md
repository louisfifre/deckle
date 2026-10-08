---
description: Foreground module — one event-driven snapshot of the foreground window (process, monitor, fullscreen stage) shared by every module that reacts to it.
type: agent-instructions
---

# AGENTS.md — Deckle.Shell.Foreground

Tells the rest of Deckle which window is in front, which process owns it, which monitor it sits on and whether it reads as fullscreen. A leaf over Deckle.Core and Deckle.Diagnostics, so non-UI modules can depend on it without pulling the WinUI shell.

Two WinEvent hooks, event driven only: `EVENT_SYSTEM_FOREGROUND` globally for app switches, and `EVENT_OBJECT_LOCATIONCHANGE` narrowed to the foreground window's process (`idProcess`, re-armed on each switch) for in-place moves and F11 toggles. The narrowing keeps one global location stream in the process, the taskbar cover's cursor hook; never arm it with process id 0, which means every process.

Threading: `Start` runs on a thread that pumps messages (the UI thread in Deckle.App); `Stop` and `Dispose` run on that same thread because `UnhookWinEvent` fails elsewhere. `Changed` is raised on it. `Start` returns false and logs when the foreground hook fails; a failed location hook only logs. `Current` is an immutable `ForegroundState` swapped by reference, readable from any thread, `ForegroundState.None` before `Start` and after `Stop`.

Fullscreen is the taskbar cover's proven two-stage test: `SHQueryUserNotificationState` first, then DWM extended frame bounds covering the monitor (`FullscreenGeometry.CoversMonitor`, the only unit-tested part), the desktop windows excluded. `ProcessName` is the executable name without extension, as `Process.ProcessName` spells it; `Monitor` is the `HMONITOR`, comparable with Vision's monitor handle.

Consumers: the taskbar cover today, which compares `Monitor` with its band's monitor; Ambient and the games registry are meant to read the same snapshot rather than probe the foreground themselves.

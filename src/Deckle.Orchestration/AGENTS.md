---
description: Apps and games domain module — the registry of apps the user cares about, noticing when they run, and applying their rules while they do.
type: agent-instructions
---

# AGENTS.md — Deckle.Orchestration

This module keeps one registry of apps and games (`Registry/AppEntry`), persisted in `modules/orchestration/settings.json`, and its Settings page "Apps and games". It notices when they run and applies their rules while they do (HDR per monitor first).

**Sources.** An entry comes from one of three kinds of source: added by hand (an executable, or a running process), found in a Steam library, or found in a folder the user chose to scan. `AppSource` records which one.

**Matching.** `AppMatcher` is the only place that decides whether a process is an entry. Precedence: same executable path, then an image path under the entry's install directory, then the same process name (exe stem, case-insensitive). Disabled entries never match. Keep it pure and tested.

**No poll.** Detection is event-based only (foreground change, process start events, process exit handles). Never add a timer that enumerates processes.

**Detection sources.** `Detection/OrchestrationRuntime` feeds `AppSessionTracker` from three sources: `IForegroundMonitor` (implemented by the shared foreground service; `NullForegroundMonitor` until the App adapts it), `WmiProcessStartSource` (`Win32_ProcessStartTrace`), and `ProcessExitWatch` (a `SYNCHRONIZE` handle waited on by the thread pool). WMI start events only work when Deckle runs elevated; unelevated, the page advises "Start elevated". The only process enumeration is one pass per reconcile (boot, registry change).

**No master toggle.** Each entry's `Enabled` switch is the control; the runtime runs only while at least one entry is enabled. A module-wide switch would hide the list and add a setting nobody asked for.

**Rules are transactional.** Every per-app rule is an `IRuleEffect` whose `Apply` returns its own undo; `ProfileTransaction` applies them at session start and rolls back on any failure, and the runtime restores on session end and on `Stop`, in reverse, one failed restore never blocking the others.
An effect changes the live system only, never another module's settings; new effects (autocorrect, Ambient) plug in through `IRuleEffect`. Rules apply at launch: editing them while the app runs affects the next launch.

**Mutations swap references.** `OrchestrationSettingsService` replaces `Entries` with a new list under its write lock, never edits it in place, so runtime readers see a complete old or new list.

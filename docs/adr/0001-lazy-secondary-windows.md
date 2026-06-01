---
name: adr-0001-lazy-secondary-windows
description: "Records that Deckle builds its secondary WinUI 3 windows (Settings, Logs, Playground) lazily on first open while the HUD stays eager at boot, because building all windows at startup caused initialization crashes. Read before proposing to build windows eagerly to fix a first-render symptom."
type: adr
---

# ADR-0001 — Lazy secondary windows, eager HUD, for boot stability

**Status** — accepted 2026-04-15

## Context

Deckle exposes several WinUI 3 windows: the HUD (the hot path of the transcription hotkey), Settings, Logs, Playground. Initially all were built at startup in `App.OnLaunched`. This caused runtime stability problems — crashes at initialization, not mere performance loss. The diagnosis never isolated a single root cause, but it established the empirical fact: boot-all-windows was not viable. Switching to a lazy pattern eliminated the crashes.

## Options considered

- **A. All windows at boot.** The initial scheme — simple to reason about, but it crashes at startup.
- **B. All windows lazy.** Built only on user open. Eliminates the crashes but adds visible latency on the first transcription-hotkey press: the HUD would have to be built hot, while invisible.
- **C. Lazy for secondaries, eager for the HUD.** Settings, Logs, Playground built on demand via `App.ShowXxxLazy`; the HUD built at boot in `OnLaunched` to preserve hot-path latency.

## Decision

Option C. The HUD stays built at boot because it is on the transcription hotkey's hot path. Settings, Logs and Playground become lazy via `App.ShowSettingsLazy`, `ShowLogsLazy`, `ShowPlaygroundLazy`. The HUD is the justified exception — the criticality of its appearance latency, not an immunity to the boot-all-windows problem.

## Consequences

Startup stability is restored. For cosmetic first-render issues (first-open latency of a secondary window, flicker), the doctrine is just-in-time techniques — off-screen prewarm before first show, font preload via DirectWrite — not a return to the all-windows-at-boot scheme.

No proposal to revert to all-at-boot in order to fix a first-render symptom MUST be accepted without re-investigating the original crashes. The decision is hard until proven otherwise by evidence; should evidence ever overturn it, a new ADR supersedes this one.

---
name: claude-deckle-diagnostics-logging
description: "Doctrine for Deckle.Diagnostics.Logging (live logging settings and AmbientCaptureGate). Read before touching the LogWindow surface settings or the ambient capture noise gate."
type: agent-instructions
module: Deckle.Diagnostics.Logging
---

# CLAUDE.md — Deckle.Diagnostics.Logging

Child module of `Deckle.Diagnostics` that owns the **live journal settings** shared by the LogWindow and application-journal persistence. The XAML viewer currently lives in `Deckle.App`; this module carries the persisted selector/noise-filter settings used by the viewer and by the `app.jsonl` predicates.

The module depends on `Deckle.Diagnostics` (sink interfaces, EventEntry) and `Deckle.Core` (AppPaths for the per-module settings file). No dependency on the legacy `Deckle.Logging`.

## Current responsibilities

`LoggingSettings` carries user choices for the live journal:

- **SelectorBar filters** — selection by level family (`All`, `Activity`, `Alerts`) for the viewer. The selected family is persisted in `LoggingSettings.LogWindowVisibilityMode` and reused by the application-journal JSONL predicate so future `app.jsonl` lines follow the same broad visibility mode. Search text remains UI-local and does not gate disk writes.
- **Capture loop noise** — `LogAmbientCaptureActivity` (bool); when off and a capture loop is active, `Verbose` events from the ambient providers (ambient, vision, lighting) and the per-frame `Deckle.Diagnostics.Resource` firehose (D3D11 texture acquire/release, ~2 per frame) are dropped by the live LogWindow listener and by the `app.jsonl` predicate. Resource is a transverse sub-provider, so during capture the HUD's own Composition-visual Resource `Verbose` is silenced too — an accepted trade-off of the provider-level (pre-`BuildEntry`) filter; a future dedicated "firehose" category would gate by `owner` instead.

`LoggingSettingsService` is the per-module persistence singleton that loads and saves the POCO under `<UserDataRoot>/modules/logging/settings.json`. Pattern aligned with the other `*SettingsService` of the project.

## Boundary with `Deckle.Diagnostics.Telemetry`

The split is by **human vs machine consumer**. Everything that touches the interactive viewer's persisted projection and capture-noise filter lives here. Everything that touches structured telemetry files and disk-persistence gates (`ApplicationLogToDisk`, latency, microphone, corpus) lives in `Deckle.Diagnostics.Telemetry`. Both modules depend independently on `Deckle.Diagnostics`; they do not reference each other.

## LogWindow boundary

`ILogWindowSink` stays in the parent diagnostics module. `Deckle.App.LogWindow` implements the sink today and applies the selector/search projection locally. `LoggingSettingsService` persists only the settings that must survive process restarts.

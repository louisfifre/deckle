---
name: claude-deckle-diagnostics-logging
description: "Doctrine for Deckle.Diagnostics.Logging (live logging settings and AmbientCaptureGate). Read before touching the LogWindow surface settings or the ambient capture noise gate."
type: agent-instructions
module: Deckle.Diagnostics.Logging
---

# CLAUDE.md — Deckle.Diagnostics.Logging

Child module of `Deckle.Diagnostics` that owns the **LogWindow surface** — the live event viewer window, its user filters, and the optional disk persistence of the application journal. The XAML viewer itself will be ported from `src/Deckle/Ui/LogWindow*` (movement E of the modular pass); in wave 1 this module only contains the settings and a bridge sink so the legacy LogWindow receives events emitted by the new EventSources.

The module depends on `Deckle.Diagnostics` (sink interfaces, EventEntry) and `Deckle.Core` (AppPaths for the per-module settings file). No dependency on the legacy `Deckle.Logging`.

## Current responsibilities

`LoggingSettings` carries user choices for the live journal:

- **SelectorBar filters** — selection by level (Critical, Error, Warning, Informational, Verbose) and by module for the viewer.
- **Persistence gate** — `ApplicationLogToDisk` (bool), gate that controls whether the general channel's `JsonlEventListener` writes to `app.jsonl`. Off by default in preview, assumed on in local debug.
- **Capture loop noise** — `LogAmbientCaptureActivity` (bool); when off and a capture loop is active, `Verbose` events from ambient providers (vision, lighting) are dropped before emission. Mirrors the posture of the legacy `TelemetryService._captureActive` but carried as a listener-side filter rather than hub-side.

`LoggingSettingsService` is the per-module persistence singleton that loads and saves the POCO under `<UserDataRoot>/modules/diagnostics-logging/settings.json`. Pattern aligned with the other `*SettingsService` of the project.

## Boundary with `Deckle.Diagnostics.Telemetry`

The split is by **human vs machine consumer**. Everything that touches the interactive viewer (SelectorBar filters, text formatting, application journal gate) lives here. Everything that touches structured telemetry files (latency, microphone, corpus, consent dialogs) lives in `Deckle.Diagnostics.Telemetry`. Both modules depend independently on `Deckle.Diagnostics`; they do not reference each other.

## Progressive migration of the LogWindow

In wave 1 the module does not yet expose a XAML window — only `LoggingSettings` and the concrete implementation of `ILogWindowSink` that forwards to the legacy LogWindow installed by the App. When the LogWindow itself is ported here (surface wave, later modular milestone), the concrete sink will become a direct method on the window's ViewModel, and the legacy bridge will disappear.

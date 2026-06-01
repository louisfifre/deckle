---
name: adr-0004-diagnostics-parent-logging-telemetry-children
description: "Records the three-module split of observability: a Deckle.Diagnostics parent carrying the shared EventSource plumbing, and two children Deckle.Diagnostics.Logging (live viewer) and Deckle.Diagnostics.Telemetry (structured persistence + consent). Read before placing a new diagnostics type or growing the modules."
type: adr
---

# ADR-0004 — Diagnostics parent, Logging and Telemetry children

**Status** — accepted 2026-05-22

## Context

ADR-0003 introduces `EventSource` as the observability pillar. It remains to decide where the shared technical plumbing (abstract base class, cross-cutting keywords, EventListeners, sink interfaces) lives relative to the consumer surfaces (the live LogWindow, the structured telemetry files and their consent dialogs).

The current `Deckle.Logging` module mixes both dimensions: the emission hub, the concrete sinks, the structured payloads, the logging and telemetry user settings, and the gate interfaces. During the project's first year this grouping was legible. The observability rework forces clarification: the emission pillar is consumed by *every* module, whereas the consumer surfaces are themselves separate feature areas — a live interactive viewer, and a structured persistence with user consent.

## Options considered

- **A. A single `Deckle.Diagnostics` that carries everything.** Simple continuity with the legacy pattern. But the module grows large and heterogeneous, and leaf modules that need only the base class would transitively depend on the consent dialogs and persistence configuration.
- **B. A parent `Deckle.Diagnostics` + a single `Deckle.Diagnostics.UI`** grouping the surfaces and settings. Separates plumbing from surface, but artificially groups the live viewer (LogWindow) with the structured persistence — the two share nothing on the human-consumer side.
- **C. A parent `Deckle.Diagnostics` + two children** `Deckle.Diagnostics.Logging` and `Deckle.Diagnostics.Telemetry`. The parent carries the plumbing consumed by all emitters (`DeckleEventSource`, the shared `Keywords` enum, the sink interfaces, the EventListener implementations). The `Logging` child carries the viewer surface (LogWindow, ViewModels, filters, the `ApplicationLogToDisk` gate). The `Telemetry` child carries the structured persistence (settings, consent dialogs, boot configuration of the JSONL listeners with their file paths).

## Decision

Option C. The observability pipeline is organized in three modules: a parent `Deckle.Diagnostics` carrying the technical plumbing, consumed by all emitting modules; two children `Deckle.Diagnostics.Logging` (viewer surface + app-log gate) and `Deckle.Diagnostics.Telemetry` (structured persistence + consent), consumed only by the host app at boot.

## Consequences

Easier: a leaf module that wants to emit depends only on `Deckle.Diagnostics` (the plumbing), not on the consumer surfaces; the dependency graph stays minimal; the two children evolve at their own pace.

Harder: three modules to create and maintain instead of one — a real administrative overhead (three `csproj`, three `CLAUDE.md`, three times the boot ceremony in the host app). Offset by the clarity of the graph and by the three roles being genuinely distinct.

Impossible: a consumer of the plumbing transitively depending on a XAML surface — the parent references neither WinUI 3 nor the surface modules. The host app references the three modules and wires the listeners at boot via `AppDiagnosticsBootstrap.Initialize(...)`.

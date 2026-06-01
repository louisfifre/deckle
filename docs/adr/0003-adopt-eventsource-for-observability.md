---
name: adr-0003-adopt-eventsource-for-observability
description: "Records the move of Deckle's observability pillar from a home-grown TelemetryService hub to System.Diagnostics.Tracing.EventSource, for call-site typing, standard ETW interoperability and native testability. Read before adding or reshaping an emission path."
type: adr
---

# ADR-0003 — Adopt EventSource for observability

**Status** — accepted 2026-05-22

## Context

Deckle's observability pillar rested on a home-grown `TelemetryService` hub coupled to interchangeable `ITelemetrySink` sinks. It centralizes emission well, but it had accumulated weaknesses that surface as the project enters a broader instrumentation-and-test phase.

The emission API is not typed at the call site. Everything goes through `TelemetryService.Log(source, message, level, feedback)` or one of three structured methods taking a record POCO; `source` is a `string`, `message` is free-form, the level is hand-chosen by the caller. Nothing in the BCL enforces consistency — a wrong constant or level passes only through human review. The `TelemetryEvent` format is project-internal: no external tool reads it, and the runtime trace is not consumable by PerfView, dotnet-trace, or standard ETW tooling. The emitter↔sink coupling is explicit and runtime. Writing tests on a module's emitted event sequence requires mocking an `ITelemetrySink` into the pipeline — there is no native test contract.

## Options considered

- **A. Keep `TelemetryService` and evolve it in place.** Low short-term cost, but continued debt on typing and external interoperability, and no gain on native tests or ETW integration.
- **B. Adopt `System.Diagnostics.Tracing.EventSource`.** A native .NET typed-tracing mechanism, ETW-normalized, supported by Microsoft tooling (PerfView, dotnet-trace, dotnet-counters). Each module declares its own `EventSource`, with one `[Event]` method per distinct operation — the signature is typed and static, the compiler rejects an inconsistent one. EventListeners are framework classes, attachable at boot and at runtime, with no proprietary contract. Learning and migration cost up front; durable gains on typing, interoperability and native testability.
- **C. Adopt a third-party library (Serilog, NLog, Microsoft.Extensions.Logging).** Mature, large sink ecosystem, but external dependencies, a posture less aligned with the project's "native primitive first" doctrine, no direct ETW integration, and an `ILogger<T>` semantics that stays textual rather than typed per operation.

## Decision

Option B. Deckle's observability pipeline moves to `EventSource`. Each module exposes a `Deckle<Module>Source` inheriting an abstract `DeckleEventSource` base that carries the session id and the ETW self-describing format. Live and file destinations are standard `EventListener`s attached at boot. Each emission becomes typed: one `[Event(...)]` method per distinct operation, with `snake_case` parameters that become the JSON keys in the JSONL output.

## Consequences

Easier: static call-site typing catches inconsistencies at build; the runtime trace is readable by any standard ETW tool with no Deckle-specific code; observability tests attach a collector `EventListener` without touching the module — a native contract. The single-emission discipline is carried by the ETW runtime (a module that does not declare its `EventSource` cannot emit), no longer by human review.

Harder: migrating the existing `TelemetryService`/`LogService` call sites, which are numerous and scattered; the ETW signature forbids complex parameter types, so the structured payloads must be flattened into primitive parameters at each emission; the legacy central "capture-active" runtime filter must be re-implemented as a provider- or listener-side filter.

Impossible: a generic `Log(source, message, level)` escape hatch that would rot the typing discipline — `DeckleEventSource` exposes no such API; every event must be declared as an `[Event]` method on the module's provider.

The emission convention and parameter rules live in the reference sheet [`reference--eventsource-convention--1.2.md`](../reference/reference--eventsource-convention--1.2.md).

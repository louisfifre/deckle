---
description: Observability foundation — EventSource providers, levels, sinks.
type: agent-instructions
---

# AGENTS.md — Deckle.Diagnostics

No dependency on `Deckle.Core`, by design: diagnostics sits under every other module, app paths included.

## Provider convention

- One `Deckle<Module>Source` per emitting module: ETW name `Deckle-<Module>`, static `Log`, `sealed`, inherits `DeckleEventSource`.
- The ETW name takes a dash, never a dot, to keep the emitter identity distinct from the `Deckle.<Module>` namespace.
- Keyword bits 0–9 are cross-cutting; higher bits are the provider's.
- One `[Event]` per call-site operation. No generic `Log(string, level)` channel.
- Transverse sub-providers (`Deckle<X>Source`) live here. Promote one only when two or more business modules use it with the same parameters and it is non-business platform wiring.
- Event parameters are `snake_case`, against the design guidelines: they become JSONL keys and must match the ETW manifest read by PerfView and benchmark scripts. `IDE1006` is suppressed.
- No central provider list: it would drift. One normalized source file per module lets a search rebuild the inventory.

## Levels and messages

- A transient that a retry absorbs with no visible effect is `Verbose`, not `Warning`.
- IDs and `k=v` are Verbose-only; an Info with an ID is a level mistake. Emit two events: a Capital Info without IDs, then its Verbose mirror with the IDs.

## Performance

- Check `IsEnabled()` before building any payload.
- `IsEnabled()` is not the user verbosity policy. A governed activity checks its control at the producer, before any log-only probe, measurement or payload. Dropping at the dispatcher or a sink is too late.

## Dispatch and sinks

- `DispatchEventListener` is the only `EventListener`. Sinks are passive `ILogSink`s and never subscribe themselves.
- Disk sinks never serialize or write on the emitter thread: bounded FIFO, backpressure instead of drops, a deterministic `Flush` called by the composition root after producers stop.
- The operational log and telemetry datasets are separate authorities. A fact needed by both is two events.
- `app.jsonl` is an operational sink owned by `Deckle.Diagnostics.Logging`, not telemetry.
- HUD feedback: call the milestone event **and** `UserFeedbackEmitted`, never one for the other. Its parameters are `int` because EventSource rejects enums. A persistent alert belongs to `Deckle.Notifications`.
- One `SessionId` static on `DeckleEventSource` serves every provider.

## Durable rules not in deckle-logging

- One step is one start Info and one end Info, Verbose in between.
- A repeating degradation is one incident: raw attempts stay `Verbose`, one visible incident past an operation-specific threshold, one visible recovery.
- Heartbeats under one second are never logged.
- Every measure has a canonical unit suffix (`_ms`, `_sec`, `_us`). If one is missing, fix the convention first.
- Logs are English, single-line, and never repeat the module name.

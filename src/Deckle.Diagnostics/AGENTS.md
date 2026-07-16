---
description: Observability foundation — EventSource providers, levels, sinks, JSONL contract.
type: agent-instructions
---

# AGENTS.md — Deckle.Diagnostics

Foundation of the observability pillar: the plumbing shared by every `Deckle.*EventSource` and the listeners that consume them. Depends only on the BCL — **no dependency on `Deckle.Core`**, by design: diagnostics sits underneath every other brick, including app paths. Concrete destinations (JSONL paths, LogWindow, HUD) are injected by consumer modules at boot through the sink interfaces exposed here.

## Provider convention

One concrete EventSource per emitting module: class `Deckle<Module>Source`, ETW `[EventSource(Name = "Deckle-<Module>")]`, static singleton `Log`, `sealed`, inheriting `DeckleEventSource`. The provider name takes a **dash**, never a dot: it is an emitter identity, kept visually distinct from the homonymous `Deckle.<Module>` namespace (see `deckle-nomenclature`). Cross-cutting keywords occupy keyword bits 0–9; bits 10+ belong to the provider and stay local. One `[Event]` method per distinct call-site operation — no generic `Log(string, level)` channel, no typed-payload argument.

**Transverse sub-providers** (`Deckle<X>Source`, ETW `Deckle-<X>` — the `Diagnostics` home is a code-organization fact, not part of the emitter identity, so the umbrella does not enter the provider name) live here, not in a business module. Promotion criterion, both clauses required: (1) ≥2 business modules consume the primitive with the *same* parameter set, AND (2) it's non-business platform wiring.

Event parameters are `snake_case` — a deliberate derogation from the Framework Design Guidelines, because they become the JSON keys in the JSONL and must match the ETW manifest a third-party consumer (PerfView, benchmark scripts) reads. `IDE1006` is suppressed in the emitting csproj.

## No central registry — normalized, not listed

Sources are decentralized by design: each emitting module owns its `Deckle<Module>Source.cs`, beside the code it observes. There is deliberately no provider list in this doctrine — it would drift. The naming convention is what makes that safe: because every module has exactly one normalized source file, the full inventory is reconstructible on demand by a single search, never maintained by hand.

## Levels

Five native `EventLevel`, no custom level (legacy `Narrative` dropped — user text goes through `UserFeedbackEmitted` or a `.resw` string). `Informational` is a progress milestone as a short Capital sentence; it carries the old Info *and* Success (success is in the message, not a level). `Verbose` is machine-greppable detail. Level and admission are orthogonal: a repetitive `Informational` outcome may still belong to an off-by-default activity stream, while lifecycle milestones and incident/recovery milestones do not.

Calibration trap: a transient a retry loop absorbs on its own (no user-visible effect, attempt count low) is `Verbose`, not `Warning`. `Warning` is for a degradation a human would want to notice even though it recovers. A recurring line that always self-heals on the first retry is a miscalibrated `Warning`.

## Verbose ↔ Info separation

**IDs and `k=v` are Verbose-only.** An Info/Warning/Error/Critical `Message` is a short Capital sentence readable with no knowledge of the implementation. If it contains an ID (light id, path, hash, index) or `|` separators, it's a Verbose event by definition — an Info with an ID is a level mistake. When an action needs both a user signal and a technical detail, emit **two events**: a Capital Info without IDs, and its Verbose mirror carrying the IDs as `snake_case` parameters. The mirror always follows the Info. Raw content is not granted admission merely because it keeps native casing: transcript and typed-text content are excluded from the operational log and belong only to their explicitly consented datasets.

## Performance

Every `[Event]` is gated by `IsEnabled()` (or `IsEnabled(level, keywords)`) before any payload construction — zero allocation when no listener listens. The parameterized gate only earns its keep when it avoids a construction (string, array, computation).

`IsEnabled()` is not the user verbosity policy: Deckle's shared listener may enable a provider even when one chatty activity is disabled. An activity governed by a verbosity control checks that control **at the producer**, before any probe, measurement, payload, string, array, or counter collection whose only consumer is the operational log. Dropping the resulting event in the dispatcher or a sink is too late to satisfy the control's performance purpose.

## One dispatch, passive sinks

A single `DispatchEventListener` is the only `EventListener`: it subscribes to the whole `Deckle-*` family, builds each admitted `EventEntry` once, then offers it to every registered `ILogSink`. A sink decides whether it `Wants` the entry and how to `Write` it — it never subscribes to an EventSource itself. The dispatcher is a fan-out boundary, not the authority for user verbosity: producer-side controls have already prevented the governed operational observations and their log-only work. Per-sink recording and display filters remain sink concerns.

The operational log stream and purpose-specific telemetry datasets are separate authorities. The LogWindow and `app.jsonl` consume only admitted operational observations; dataset-only events are rejected from both even though the shared listener receives them. Telemetry routes consume only events covered by their own explicit consent. A logging verbosity control never disables a telemetry dataset, and telemetry consent never enables an operational observation that its producer gate rejected. A fact needed by both consumers is emitted as two purpose-specific events rather than leaking the dataset event into the journal.

Consumer contracts remain passive sinks:

- **HUD** — `HudFeedbackSink` watches the canonical `UserFeedbackEmitted(int severity, string title, string body, int role)` event and ignores everything else. `int` because EventSource rejects user enums; the sink re-encodes from the name-keyed payload. A site wanting transient in-app feedback calls the milestone event **and** `UserFeedbackEmitted` — no substitution. A persistent background alert belongs to `Deckle.Notifications`, never to the HUD alone.
- **Live LogWindow** — `LogWindowSink` takes admitted operational events, never dataset-only routes. User filtering happens later on the UI side. It owns the boot-history ring buffer and replays it when the window attaches lazily.
- **Application JSONL** — the rotated, self-describing `app.jsonl` mirrors admitted operational events after its independent persisted recording filter. It lives under the diagnostics directory and belongs to Logging's contract.
- **Telemetry JSONL** — one routed payload-only sink per purpose-specific dataset, with an explicit consent predicate and frozen envelope. Dataset wiring lives in `Deckle.Diagnostics.Telemetry`; these events never enter the operational sinks.

A single `SessionId` (`YYYY-MM-DD-XXXX`) is generated on the first emission and shared by all providers as a static on `DeckleEventSource`, so sinks group rows by process session without threading a parameter everywhere.

## Durable rules

- One step = one start Info, one end Info; Verbose in between if needed, never repeated Infos.
- A repeating degradation is one incident, never one `Warning` or `Error` per attempt. Raw failed attempts stay `Verbose` while the operation is still recovering within its normal tolerance; open the visible incident only when an operation-specific threshold makes the degradation meaningful, count or summarize further repetitions without repeating the high-severity line, then emit one visible recovery. Verbosity controls never silence `Warning` or `Error`.
- High-frequency heartbeats (< 1 s) are not logged — they feed UI events, not the LogWindow. The window carries steps, not frames.
- Every measure has a canonical unit/precision/suffix so the same thing greps everywhere (`_ms`/`_sec`/`_us` durations, `rms` 4 decimals, `dbfs` 1 decimal, …). If a unit is missing, fix the convention before using it — no ad-hoc measure.
- Logs in English, never a multi-line event, never repeat the module in the message (the Source tag already carries it).

## Tests

EventSource is testable via a custom `EventListener` wired in the test (attach, run, assert on the collected `EventEntry` sequence) — native testability that partly motivated the EventSource choice.

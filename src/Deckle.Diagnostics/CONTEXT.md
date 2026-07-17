---
name: context-deckle-diagnostics
description: "Observability vocabulary for the Diagnostics family (.Logging, .Telemetry included) — admission vs view, the five controls that quiet the log stream without being confused. Read before touching verbosity, filters, or sinks."
type: agent-instructions
---

# Deckle.Diagnostics — Context

Observability vocabulary shared by the Diagnostics family — `Deckle.Diagnostics`, `Deckle.Diagnostics.Logging`, `Deckle.Diagnostics.Telemetry`. Two controls can make the LogWindow quieter, but they act on opposite sides of the journal boundary. The distinction is whether an observation still exists after the control acts.

## Admission vs view

**Verbosity control** :
A persistent, per-activity admission policy for Deckle's operational log stream, evaluated at the producer before log-only work. When off, the governed observations reach no log sink and their probes, computations, allocations, formatting, and payload construction do not run. The policy may govern repetitive `Informational` outcomes as well as `Verbose` detail; workflow lifecycle, incidents, recoveries, their one-shot technical mirrors, and every `Warning`/`Error` remain. Purpose-specific telemetry datasets are a separate authority.
_Avoid_ : capture control (capture already names audio and screen acquisition), view filter (which never removes an observation).

**Application log** :
The optional on-disk mirror of Deckle's admitted operational log stream, currently `diagnostics\app.jsonl`. It may apply its own persisted recording filter, but it is not a telemetry dataset.
_Avoid_ : telemetry (purpose-specific data collection), LogWindow log (the other sink of the same stream).

**Telemetry dataset** :
A purpose-specific machine-readable data channel governed by its own explicit consent, such as latency, microphone, corpus, or autocorrect-decision data. It is outside the operational log stream and its verbosity controls.
_Avoid_ : application log, logs.

**View filter** :
A process-lifetime LogWindow lens over observations already admitted to the journal. It changes only what the window currently shows; it never changes or destroys the journalled observations.
_Avoid_ : verbosity control (which acts before the journals), capture filter.

**Recording filter** :
A persisted, sink-local selection over already admitted operational observations. The `app.jsonl` recording filter reuses the LogWindow's `Severity` / `Module` / `Category` model but is an independent instance, has no text search, and affects only future disk entries. It can be configured while recording is off.
_Avoid_ : view filter (temporary and display-only), verbosity control (prevents the observation from existing).

**View condensation** :
A reversible LogWindow projection that groups recurring admitted `Verbose` observations into a summary while keeping every raw observation available. It improves the reading surface; higher-level repetition always remains a producer defect rather than something the view hides.
_Avoid_ : deduplication (nothing is deleted), verbosity control (nothing is refused admission).

### Example conversation

> — Ambient is flooding the journal. I do not want those detailed observations recorded at all.
> — Turn off its verbosity control. Its milestones and failures remain, but the governed `Verbose` stream enters neither journal.
> — I only want to inspect Ambient failures for a minute.
> — Use a view filter. The other observations stay in the journal and reappear when you clear the filter.
> — These admitted heartbeats still repeat while I investigate.
> — Use view condensation. The window groups them, but the raw observations remain expandable.

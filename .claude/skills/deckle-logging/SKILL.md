---
name: deckle-logging
description: Observability doctrine for Deckle: emission centralization, the split between readable milestones and structured detail, and the procedure for deciding what to observe. Canonical frames (USE/RED/Four Golden Signals) live in taxonomy.md. Invoke before adding or changing an observation point. Triggers like deckle logging, observability, what to log here, log level, instrumentation, telemetry.
type: skill
---

# Deckle — Observability doctrine

## Role

Project-specific skill that answers two questions: **what to observe in a piece of code being instrumented**, and **how to write it so that it is readable and actionable**. Invoked before adding an observation point, changing a level, or reorganizing what is emitted somewhere.

Upstream decision support ("what deserves to be observed here?") is the heart of the skill. The writing standard ("how do I formulate this event?") is its second face. The doctrine is invariant to the underlying technical engine — it stays true regardless of the emission system chosen.

## Centralization doctrine

Every runtime observation goes through **a single emission source**. No parallel path in application code — no ad hoc file write, no console output, no duplicated logger. If an observation deserves to exist, it goes through the canonical source; if it needs to land in a new place, that's an additional sink registered with the canonical source, not a new emission path.

The rule exists because maintaining two or three parallel paths inevitably causes formats, nomenclature, levels, and gaps to diverge. The central system exists precisely so that two separate systems don't have to be managed.

Drift signal to recognize in oneself: as soon as an intent appears to "create a logger for X" or "write to a dedicated file for Y", ask the prior question — doesn't the canonical source already cover this need via a sink or an additional channel? In nearly all cases, yes.

**Single subordinated exception**: the unrecoverable native crash that kills the process before sinks have had a chance to write. Ad hoc instrumentation pattern with direct file write, **temporary**, never committed as-is. For everything else, the centralization rule holds.

## Level separation doctrine

Two distinct families coexist and never mix.

**The concise readable family** — informations, successes, warnings, errors, criticals. Short, simple sentences read as milestones by a human following the flow: "Loading the model", "Recording finished", "Cannot connect to service". No `key=value`, no technical identifiers, no numeric measurements in the text — just the milestone.

**The structured detail family** — the verbose level. Receives measurements, identifiers, parameters, latencies, dimensions, return codes. Machine-greppable structured format, several lines if grouping semantically helps, first word lowercase to stand apart from the milestones above. Group an operation's observables into 3-4 verbose lines, not one line per variable.

**Articulation of the two families.** Depending on the code sequence, the verbose detail precedes or follows the concise milestone. When verbose captures parameters that lead to a decision (for example detecting an error condition), it precedes the information or alert milestone that follows. When verbose details the measurements associated with a milestone (for example the durations of an operation that just finished), it follows the milestone. This articulation makes the narrative sequence natural to read in the live window.

**Three recurring pitfalls to avoid**: putting `key=value` in a sentence of the concise family (sign that a mirror verbose should be emitted); multiplying milestones of the concise family for a single operation (sign that they should be merged into a single milestone with a detailed verbose); forgetting to instrument in verbose a step announced as a milestone (sign that diagnostic material is being lost).

## Maximum coverage

The maintainer wants **a lot of logs, well sorted**: expose every observable measurement, not the minimum. Filtering by level, category, or term is free at read time; re-instrumenting after the fact, when a bug needs diagnosing, is expensive. Pair this with runtime control — a general toggle, plus a few for particularly chatty subsystems (high-frequency capture, microphone telemetry, user corpus). Everything is instrumented; what to look at and what to persist is chosen afterward.

## Decision procedure

When about to instrument a piece of code, four questions in order.

**Which module is concerned.** The observation attaches to the module that contains the operation, not the module that calls. This attribution conditions where the event appears in the overall map.

**What category of code is being instrumented.** High-frequency real-time loop? One-shot or batch pipeline? Hardware driver or external integration? User interface surface? Application lifecycle? The category determines which canonical frames apply and which observables are relevant.

**Which observables are relevant for this category.** This is where the `taxonomy.md` reference is loaded and consulted: it gives the industry canonical frames (utilization, saturation, errors for resources; rate, errors, duration for pipelines; latency, traffic, errors, saturation for surfaces that serve) and their application to the Deckle profile, with the sub-natures of observables and examples.

**Which typed events to emit from it.** For each relevant observable, decide on the level and the wording. Structured technical details in verbose, concise milestones in information or warning or error. If the operation has an end-of-operation rollup (one line per operation with all its fields colocated), this rollup carries the real diagnostic material — the intermediate milestones mark its reading but do not duplicate its content.

## Closed vocabulary

Observation sources, units of measurement, operation names are closed vocabularies — no ad hoc creation, no spelling variation. If a unit is missing (because a magnitude never observed until now is being observed), add it to the canonical vocabulary before use, not just inside the new event. The discipline of closed vocabulary guarantees that a human or an agent filtering on a term finds exactly the same thing everywhere.

## Pointers

- **`taxonomy.md`** in this skill — canonical observability frames and mapping to the categories of code encountered in Deckle. Loaded on demand when the decision procedure reaches the "which observables" step.
- **`session-save-context`** — when an instrumentation decision is non trivial, route it through the cascade for a durable trace.
- **`deckle-workflow`** (section *Code comments*) — comment hygiene when annotating an instrumentation site.
- **`personal-conventions`** — cross-project convention of logging centralization (single source, interchangeable sinks). `deckle-logging` applies this convention to the Deckle project and refines it.

---
name: deckle-logging
description: What to observe in code, and how to write it readable and actionable. Invoke before adding or changing an observation point.
type: skill
---

# Deckle — Observability

## Intent

Decide what to observe in a piece of code being instrumented, and write it so a human reads it as a clear, actionable narrative.

## How

Every runtime observation goes through one central emission source — instrument wherever you need, everything lands in one place. A new destination is another sink on that source, never a new path. The canonical source must cover the need.

Two families never mix. Concise milestones — short sentences a human reads while following the flow ("Loading the model", "Cannot reach the service"), no key=value, no numbers. Structured verbose — measurements, identifiers, latencies, greppable, grouped a few lines per operation. Verbose precedes the milestone when it carries the parameters of a decision, follows it when it details what just happened.

Instrument generously and sort well: expose every useful observable, not the minimum — re-instrumenting to chase a bug later is expensive. Ordinary observations enter the journals and can be filtered or condensed at read time without loss. Explicit verbosity controls are different: they are persistent admission policies for designated chatty `Verbose` streams and refuse those observations before every journal; they never silence milestones, warnings, or errors.

Observation sources, units, operation names are closed vocabularies — no ad hoc creation, no spelling variants. A genuinely new magnitude is added to the canonical vocabulary before first use, so filtering on a term finds the same thing everywhere.

The only question to keep asking: is there anything left to instrument — anything observable not yet surfaced in the logs?

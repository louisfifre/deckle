---
name: deckle-logging
description: Use when you add or change an observation point in Deckle code, or write code that starts, skips or abandons work, to write what a person can read and act on later, from a machine they cannot reach.
metadata:
  version: "1.1"
---

## Why

A person reads Deckle's log later, about a machine they never touched, with only what the defaults wrote to disk. From that record alone they have to see what ran, what was chosen, how each piece of work ended, and what failed and why.

## Statement

The record that travels is the log written whatever the controls say: it keeps every Warning and Error, and every milestone, an event that carries the Milestone tag. The rest is detail, admitted by the verbosity control that `src/Deckle.Diagnostics/GLOSSARY.md` defines, and kept on disk only by the trace the person turns on.

Work that lasts, from the process itself down to a loop or a model load, writes its start before it begins and its end with its outcome, and an end that nothing declared reads as abandoned. Both carry the Milestone tag and an identifier that pairs them in the file alone. A failure is a Warning when the work went on degraded and an Error when it stopped; it carries its cause, the step and the error, in one entry. Work that does not happen says so with its reason, tagged like a start, and a fallback is a Warning naming what was asked and what was obtained.

A message reads by its level. At Informational it is a sentence alone, at Verbose `name={n}` pairs only, separated by ` | `, and at Warning and above a sentence, a line break, then the pairs; the values travel in the payload whatever the message shows. `TemplateDoctrine` holds every provider to it.

A degradation that repeats is one incident, closed by one recovery. A loop that runs many times a second counts, a periodic rollup carries the count, and a loop whose count stops moving shows as a Warning incident.

A line describes the machine: identifiers, counts, durations, paths relative to a known root, and from a provider that reads Home the error type and code. What the person said, typed, captured or keeps in Home stays out, as does any secret.

Every term a person filters on is spelled one way across providers and lives in its enum.

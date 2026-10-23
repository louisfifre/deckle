---
name: glossary-deckle-diagnostics
description: "Observability vocabulary for the Diagnostics family, .Logging and .Telemetry included. It separates admission from view and names the controls, filters, logs and datasets that quiet or split the log stream, so that none is mistaken for another. Read before touching verbosity, filters, or sinks."
---

# Deckle.Diagnostics, Glossary

Observability vocabulary shared by `Deckle.Diagnostics`, `Deckle.Diagnostics.Logging` and `Deckle.Diagnostics.Telemetry`. Several devices can make the LogWindow quieter, but they act on different sides of the admission boundary. What tells them apart is whether an observation still exists after the device acts. The words follow the kind of device, a control decides admission, a filter narrows what one reader sees, a log is a recorded stream and a dataset is a collection with its own purpose.

## Admission vs view

**Verbosity control** :
A control that decides, per activity and persistently, which observations Deckle admits into its operational log stream, before any work done only for the log. When it is off, the governed observations reach no log sink, and the work that would have produced them does not run. It may govern repetitive `Informational` outcomes as well as `Verbose` detail. Workflow lifecycle, incidents, recoveries, their one-shot technical mirrors and every `Warning` or `Error` always remain. Telemetry datasets answer to a separate authority.
_Avoid_ : capture control (capture already names audio and screen acquisition), view filter (which never removes an observation).

**Trace** :
The file `diagnostics/trace.jsonl`, an optional copy of Deckle's admitted operational stream kept on disk. It may have its own recording filter. It is not a telemetry dataset.
_Avoid_ : application log (its label in Settings), telemetry (purpose-specific data collection), LogWindow log (the other sink of the same stream), log (the always-on file, which keeps only what travels).

**Log** :
The always-on file `diagnostics/log.jsonl`, the record that travels to a reader on another machine. It keeps every admitted `Warning` and `Error`, every milestone and the crash details, whatever the verbosity controls say. It needs no setting and stays small by design.
_Avoid_ : trace (the opt-in full copy), LogWindow log (the live view), operational log stream (everything admitted, of which the log keeps a part).

**Milestone** :
One of the few `Informational` events that tell a run's story, such as a start and its end, a module up or skipped, or shutdown. Its provider marks it with the Milestone tag where it is emitted, so that the log keeps it at any verbosity; no level or control grants the tag.
_Avoid_ : verbosity control (which never removes a milestone).

**Telemetry dataset** :
A dataset collected for one purpose, machine-readable and under its own explicit consent, such as latency, microphone, corpus or autocorrect-decision data. It lies outside the operational log stream and its verbosity controls.
_Avoid_ : trace (the operational stream on disk), logs (too loose).

**View filter** :
A filter on what the LogWindow shows among observations already admitted to the operational log stream, for the life of the process. It changes only what the window shows and never changes or destroys the admitted observations.
_Avoid_ : verbosity control (which acts before any sink), capture filter (acts on audio or screen capture).

**Recording filter** :
A filter on what one sink records among the admitted operational observations, persistent and for that sink only. It affects only entries written after it changes.
_Avoid_ : view filter (temporary and display-only), verbosity control (prevents the observation from existing).

**Capture control** :
A control over what Deckle captures, in the audio or screen sense of capture. The control that admits or refuses log observations is the verbosity control.

**Telemetry** :
In ordinary use, data collected automatically about how a system behaves. In Deckle each such collection is a telemetry dataset, with its own purpose and consent.

**LogWindow log** :
The log that the LogWindow shows, the other sink of the same operational stream. The copy on disk is the trace.

**Logs** :
In ordinary use, any record of what a program did. The word is too loose in Deckle, where the log, the trace, the operational log stream, its sinks and the telemetry datasets are separate things.

**Log entry** :
One record in a log, one line in a file or in the LogWindow. In Deckle an entry is an observation, built once as an `EventEntry` and offered to every sink.
_Avoid_ : event (what happened, which an entry records).

**Event** :
What happened, declared by a provider with named fields. One emission becomes one log entry in each sink that takes it.

**Provider** :
The source that declares and emits a family of events, one `EventSource` per area, such as `Deckle-Chrono`. A reader filters on it to see one area.

**Level** :
The severity of an entry, the .NET `EventLevel` names from `Critical` through `Error`, `Warning` and `Informational` to `Verbose`. A `Warning` says the work went on degraded, an `Error` that it stopped. The level sets the shape of an event's message: a sentence alone at `Informational`, `name={n}` pairs only at `Verbose`, and at `Warning` and above a sentence, a line break, then the pairs.

**Structured logging** :
Logging where each entry carries named fields, such as time, level, provider and identifiers, so that a machine can filter entries and pair a start with its end. Deckle writes its files as one JSON object per line.

**Sink** :
A destination for admitted entries, such as a file, the LogWindow or the HUD. Each sink takes, by its own test, the part of the stream it serves.

**Capture filter** :
A filter applied at the moment of capture, in the audio or screen sense. A temporary lens over the LogWindow is a view filter.

### Example conversation

> - Ambient is flooding the LogWindow. I do not want those detailed observations recorded at all.
> - Turn off its verbosity control. Its milestones and failures remain, but the governed `Verbose` stream reaches no sink.
> - I only want to inspect Ambient failures for a minute.
> - Use a view filter. The other observations stay in the window and reappear when you clear the filter.

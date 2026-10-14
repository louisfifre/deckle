---
name: glossary-deckle-diagnostics
description: "Observability vocabulary for the Diagnostics family, .Logging and .Telemetry included. It separates admission from view and names the five controls that quiet the log stream, so that none is mistaken for another. Read before touching verbosity, filters, or sinks."
---

# Deckle.Diagnostics, Glossary

Observability vocabulary shared by `Deckle.Diagnostics`, `Deckle.Diagnostics.Logging` and `Deckle.Diagnostics.Telemetry`. Two controls can make the LogWindow quieter, but they act on opposite sides of the journal boundary. What tells them apart is whether an observation still exists after the control acts.

## Admission vs view

**Verbosity control** :
A control that decides, per activity and persistently, which observations Deckle admits into its operational log stream, before any work done only for the log. When it is off, the governed observations reach no log sink, and the work that would have produced them does not run. It may govern repetitive `Informational` outcomes as well as `Verbose` detail. Workflow lifecycle, incidents, recoveries, their one-shot technical mirrors and every `Warning` or `Error` always remain. Telemetry datasets answer to a separate authority.
_Avoid_ : capture control (capture already names audio and screen acquisition), view filter (which never removes an observation).

**Application log** :
An optional copy on disk of Deckle's admitted operational log stream. It may have its own recording filter. It is not a telemetry dataset.
_Avoid_ : telemetry (purpose-specific data collection), LogWindow log (the other sink of the same stream).

**Telemetry dataset** :
A machine-readable data channel with its own purpose and its own explicit consent, such as latency, microphone, corpus or autocorrect-decision data. It lies outside the operational log stream and its verbosity controls.
_Avoid_ : application log, logs.

**View filter** :
A control that narrows what the LogWindow shows among observations already admitted to the journal, for the life of the process. It changes only what the window shows and never changes or destroys the journalled observations.
_Avoid_ : verbosity control (which acts before the journals), capture filter.

**Recording filter** :
A control that selects, persistently and for one sink only, which admitted operational observations that sink records. It affects only entries written after it changes.
_Avoid_ : view filter (temporary and display-only), verbosity control (prevents the observation from existing).

**View condensation** :
A control that groups recurring admitted `Verbose` observations into a summary in the LogWindow, reversibly, while every raw observation stays available. It improves reading only. Repetition at a higher level remains a defect of the producer, which the view does not hide.
_Avoid_ : deduplication (nothing is deleted), verbosity control (nothing is refused admission).

**Capture control** :
A control over what Deckle captures, in the audio or screen sense of capture. The control that admits or refuses log observations is the verbosity control.

**Telemetry** :
In ordinary use, data collected automatically about how a system behaves. In Deckle each such collection is a telemetry dataset, with its own purpose and consent.

**LogWindow log** :
The journal that the LogWindow shows, the other sink of the same operational stream. The copy on disk is the application log.

**Logs** :
In ordinary use, any record of what a program did. The word is too loose in Deckle, where the operational log stream, its sinks and the telemetry datasets are separate things.

**Capture filter** :
A filter applied at the moment of capture, in the audio or screen sense. A temporary lens over the LogWindow is a view filter.

**Deduplication** :
The removal of repeated copies so that only one remains. View condensation deletes nothing.

### Example conversation

> - Ambient is flooding the journal. I do not want those detailed observations recorded at all.
> - Turn off its verbosity control. Its milestones and failures remain, but the governed `Verbose` stream enters neither journal.
> - I only want to inspect Ambient failures for a minute.
> - Use a view filter. The other observations stay in the journal and reappear when you clear the filter.
> - These admitted heartbeats still repeat while I investigate.
> - Use view condensation. The window groups them, but the raw observations remain expandable.

---
name: deckle-logging
description: Use when you add or change an observation point in Deckle code, an event on a provider or the call that emits it, to decide what to observe and to write it so a person can read it and act on it.
---

## Why

A person follows the running application through its log and acts on what they read. Every operational observation has to reach that log, and every line has to read as a step of the story. An observation collected for a dataset takes its own consented route, which the Diagnostics glossary defines.

## Statement

Deckle observes through EventSource. Each module derives one provider from `DeckleEventSource`, and one listener, `DispatchEventListener`, gathers every provider. A new destination is one more sink on that listener.

Observations come in two families, written apart. A milestone is a short sentence a person reads while following the flow, such as "Loading the model" or "Cannot reach the service". It is Informational, or Warning and Error when something is wrong. Its measurements, identifiers and latencies go in a Verbose event of their own, structured and greppable, a few lines per operation. The Verbose event comes before the milestone when it carries the parameters of a decision, and after it when it details what just happened.

Instrument generously. Expose every observable that helps, because re-instrumenting later to chase a bug costs more. Sort at read time. The journals keep every ordinary observation, and filters lose nothing. A verbosity control is different. It refuses a governed stream of detail before every journal, and whatever tells a person how a workflow went always passes. The Diagnostics glossary, `src/Deckle.Diagnostics/GLOSSARY.md`, defines the verbosity control and the view filter.

A repeating degradation is one incident. The raw attempts stay Verbose. One visible incident opens once the threshold the operation sets is passed, and one visible recovery closes it.

Every term a person filters on, a keyword, an activity or a unit suffix, is spelled one way in every provider. A new keyword or activity joins its enum before first use, and a unit takes the suffix the other providers already use.

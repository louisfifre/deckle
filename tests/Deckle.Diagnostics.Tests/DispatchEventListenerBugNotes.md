# DispatchEventListener bug notes

## A sink that ignored an entry closed its own incident

- **Trigger:** a sink whose `Write` throws and whose `Wants` rejects the entry the dispatcher reports the fault with (any sink that filters by provider, such as the sinks of `DispatchEventListenerIncidentTests`), seen 2026-10-22 on `ReportsASinkFaultOnceAndItsRecoveryOnce`.
- **Observed symptom:** one failing write produced `SinkFaulted` then, at once, `SinkRecovered` with a count of 1; the next failing write opened a second incident. The test saw `SinkFaulted, SinkFaulted, SinkRecovered, SinkRecovered`, each recovery counting 1 instead of the 2 failed writes (a previous run saw `SinkRecovered` first).
- **Cause (established by reading, and matching the observed sequence and counts):** `DispatchEventListener.Offer` treated "no exception" as success, including when `Wants` returned false and `Write` never ran. The `SinkFaulted` report is emitted on `Deckle-Diagnostics` from inside the fan-out, so it dispatches back through the same listener; the faulted sink did not want it, `Offer` saw no exception, and closed the incident it had just opened.
- **Violated invariant:** an incident closes only after a `Write` that returned. An entry a sink does not want says nothing about the sink's health.
- **Recurrence cue:** a `SinkFaulted` followed immediately by a `SinkRecovered` with a count of 1 while the sink is still failing; two `SinkFaulted` reports for one outage.

Regression: `DispatchEventListenerIncidentTests.ReportsASinkFaultOnceAndItsRecoveryOnce`.

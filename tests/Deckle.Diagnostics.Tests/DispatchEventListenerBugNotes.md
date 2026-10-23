# DispatchEventListener bug notes

## A sink that ignored an entry closed its own incident

- **Trigger:** a sink whose `Write` throws and whose `Wants` rejects the entry the dispatcher reports the fault with (any sink that filters by provider, such as the sinks of `DispatchEventListenerIncidentTests`), seen 2026-10-22 on `ReportsASinkFaultOnceAndItsRecoveryOnce`.
- **Observed symptom:** one failing write produced `SinkFaulted` then, at once, `SinkRecovered` with a count of 1; the next failing write opened a second incident. The test saw `SinkFaulted, SinkFaulted, SinkRecovered, SinkRecovered`, each recovery counting 1 instead of the 2 failed writes (a previous run saw `SinkRecovered` first).
- **Cause (established by reading, and matching the observed sequence and counts):** `DispatchEventListener.Offer` treated "no exception" as success, including when `Wants` returned false and `Write` never ran. The `SinkFaulted` report is emitted on `Deckle-Diagnostics` from inside the fan-out, so it dispatches back through the same listener; the faulted sink did not want it, `Offer` saw no exception, and closed the incident it had just opened.
- **Violated invariant:** an incident closes only after a `Write` that returned. An entry a sink does not want says nothing about the sink's health.
- **Recurrence cue:** a `SinkFaulted` followed immediately by a `SinkRecovered` with a count of 1 while the sink is still failing; two `SinkFaulted` reports for one outage.

Regression: `DispatchEventListenerIncidentTests.ReportsASinkFaultOnceAndItsRecoveryOnce`.

## A sink fault during boot replay stayed unreported

- **Trigger:** `AddSink(sink, replayBoot: true)` with a sink whose `Write` throws on the boot history. Reproduced 2026-10-22 by test.
- **Observed symptom:** no `SinkFaulted` on `Deckle-Diagnostics` after the sink was added; the incident was opened in the slot and stayed invisible until some later event reached the dispatcher and a state change triggered a report.
- **Cause (established by running the test):** `AddSink` ignored the value `Offer` returns when an incident opens or closes. Only `Dispatch` acted on it. The report cannot be emitted inside `AddSink`'s lock, since it dispatches back through the listener.
- **Violated invariant:** a sink that throws is reported once, as soon as the listener knows.
- **Recurrence cue:** a sink missing rows from the start of a run with no `SinkFaulted` in the log, while later rows of the same sink are present.

Regression: `DispatchEventListenerRegressionTests.A_sink_fault_during_boot_replay_is_reported_without_waiting_for_a_later_event`.

## A message template that could not be formatted vanished without a trace

- **Trigger:** an event whose `Message` template names a placeholder its payload does not carry (for example `"Value {0} and {1}"` on a one-parameter event), or a malformed brace. Reproduced 2026-10-22 by test.
- **Observed symptom:** the row reached the sinks with a null formatted message and nothing said why; a log window or text line fell back to the event name, and nobody learned that the provider's template was wrong.
- **Cause (established by running the test):** `DispatchEventListener.BuildEntry` caught every exception from `string.Format` and dropped it.
- **Violated invariant:** a degradation is reported, once, with its cause on one line. The pipeline keeps the row, which is why the catch stays, but the report is owed.
- **Recurrence cue:** rows of one event with a payload and no rendered sentence, and no `MessageTemplateBroken` for that provider and event in the log.

Regression: `DispatchEventListenerRegressionTests.A_message_template_that_cannot_be_formatted_is_reported_once_and_the_row_still_arrives`.

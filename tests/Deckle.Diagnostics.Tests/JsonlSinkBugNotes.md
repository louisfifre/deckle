# JsonlSink bug notes

## A non-finite number cost the whole row and opened a sink incident

- **Trigger:** any event whose payload carries a `double` or `float` that is `NaN` or an infinity (a ratio over a zero duration, an uninitialised measurement), written through `JsonlLineSerializer`. Reproduced 2026-10-22 by test, not seen yet in a real journal.
- **Observed symptom:** `Utf8JsonWriter.WriteNumber` threw `ArgumentException`. `JsonlSink.WriteOnWorker` caught it, closed the stream and rethrew, so the row was lost and the queue opened a `SinkFaulted` incident for a sink whose disk was fine; the next row reopened the stream and closed the incident.
- **Cause (established by running the test):** JSON has no literal for `NaN` or the infinities and the writer refuses them; the serializer passed every `float` and `double` straight to `WriteNumber`.
- **Violated invariant:** one unrepresentable value never costs a row. The serializer is total over any payload an event can carry.
- **Recurrence cue:** a `SinkFaulted` whose `error_type` is `ArgumentException` and whose message mentions "positive and negative infinity", on a sink whose destination is writable.
- **Not a bug, checked:** a lone UTF-16 surrogate in a string. The writer replaces it with U+FFFD without throwing, which `JsonlLineSerializerTests.ALoneSurrogateIsReplacedRatherThanLosingTheRow` pins.

Regression: `JsonlLineSerializerRegressionTests.A_non_finite_double_is_written_as_text_instead_of_losing_the_row` and its float sibling.

## A bounded flush waited on a stuck disk

- **Trigger:** `TryFlush(timeout)` while the destination is stuck (the worker never returns from a write), the channel is full, and a producer is blocked in `Enqueue` waiting for room. Reproduced 2026-10-22 by test.
- **Observed symptom:** `TryFlush(100 ms)` did not return; the shutdown path that calls it with a deadline hung until the disk answered.
- **Cause (established by running the test):** the blocked producer holds the queue's lifetime lock while it waits for room, and `TryFlush` took that lock with an unbounded `lock`. The deadline only covered the marker enqueue and the drain wait, not the lock in front of them.
- **Violated invariant:** a call that takes a deadline returns by that deadline, whatever the disk does.
- **Recurrence cue:** a shutdown that hangs past its flush budget with a sink queue at capacity.

Regression: `BoundedWriteQueueRegressionTests.A_bounded_flush_returns_in_time_while_a_producer_waits_on_a_full_queue`.

## A recovered write error was rethrown at flush

- **Trigger:** one failed write followed by a successful one (a disk hiccup), then `Flush`, `TryFlush` or `Dispose`. Reproduced 2026-10-22 by test.
- **Observed symptom:** the journal said `SinkRecovered`, then the shutdown flush threw the old error as if the sink were still broken.
- **Cause (established by running the test):** the queue remembered the last write error for Flush and Dispose to rethrow, and recovery closed the incident without clearing it.
- **Violated invariant:** an incident closed by `SinkRecovered` leaves nothing to rethrow.
- **Recurrence cue:** a `SinkRecovered` row followed by a flush failure naming the same error type and message.

Regression: `BoundedWriteQueueRegressionTests.A_write_error_already_reported_as_recovered_is_not_rethrown_at_flush`; the guard `A_write_error_with_no_recovery_is_still_rethrown_at_flush` keeps the dead-destination case loud.

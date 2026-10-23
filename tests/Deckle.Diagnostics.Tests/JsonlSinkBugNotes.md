# JsonlSink bug notes

## A non-finite number cost the whole row and opened a sink incident

- **Trigger:** any event whose payload carries a `double` or `float` that is `NaN` or an infinity (a ratio over a zero duration, an uninitialised measurement), written through `JsonlLineSerializer`. Reproduced 2026-10-22 by test, not seen yet in a real log.
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
- **Observed symptom:** the log said `SinkRecovered`, then the shutdown flush threw the old error as if the sink were still broken.
- **Cause (established by running the test):** the queue remembered the last write error for Flush and Dispose to rethrow, and recovery closed the incident without clearing it.
- **Violated invariant:** an incident closed by `SinkRecovered` leaves nothing to rethrow.
- **Recurrence cue:** a `SinkRecovered` row followed by a flush failure naming the same error type and message.

Regression: `BoundedWriteQueueRegressionTests.A_write_error_already_reported_as_recovered_is_not_rethrown_at_flush`; the guard `A_write_error_with_no_recovery_is_still_rethrown_at_flush` keeps the dead-destination case loud.

## A torn row glued to the next one

- **Trigger:** a file that ends in the middle of a row (process killed or disk full during a write), then the sink appends. The tear is injected by test by writing a half row without newline; the real tear between the old two-step write (row, then newline) cannot be injected through an existing seam.
- **Observed symptom:** the next row starts on the torn line, so a JSONL reader loses both the torn row and the new one.
- **Cause (established by running the test):** rows were appended as bytes then a separate newline, and the open did not look at how the file ended.
- **Violated invariant:** every row starts on a line of its own.
- **Recurrence cue:** a JSONL line that does not parse and holds two `"timestamp"` keys.

Fix: the serializer returns the row with its newline, written in one call, and `JsonlAppendStream.Open` closes a file that ends mid-row before appending.

Regression: `JsonlSinkRegressionTests.A_row_cut_short_before_its_newline_does_not_glue_to_the_next_row`.

## A file that could not roll, prune or be counted said nothing

- **Trigger:** rotation maintenance that fails while writes still succeed: the archive folder cannot be created (a file sits in its place), an old generation cannot be deleted (held open, read-only), or the active file cannot be read at start. Reproduced 2026-10-22 by test, not seen yet in a real log.
- **Observed symptom:** nothing. A failed roll left the active file growing past its cap, a failed prune left the archive over its cap, and a failed count made the file look empty so it would not roll for another full cap of lines. The first sign was a disk filling up.
- **Cause (established by running the test):** each step ended in a bare `catch` that returned a default, on purpose so the log keeps writing, with no line left behind.
- **Violated invariant:** a degradation is reported once as an incident and closed by one recovery. A sink that stops maintaining its file is a degraded sink.
- **Recurrence cue:** a `RotationFailed` (Warning) with no `RotationRecovered` after it, on a file whose size or archive count keeps rising.
- **Also changed, not covered by a test:** `NextGeneration` no longer swallows a listing error. It returned the number after the entries seen before the error, which could be 1, and the roll moves with overwrite, so a listing that failed midway could erase the oldest archive. The listing error now fails the roll instead. No existing seam makes `Directory.EnumerateFiles` fail after `CreateDirectory` succeeded.

Regression: `JsonlSinkMaintenanceTests.A_roll_that_cannot_happen_is_reported_once_and_its_recovery_once`, `An_old_generation_that_cannot_be_deleted_is_reported`, `An_active_file_that_cannot_be_counted_is_reported`.

## A routed row without a path was skipped silently

- **Trigger:** `RoutedJsonlSink` whose path resolver returns an empty path, which `TelemetryListenerBootstrap` does when a corpus event lacks `bucket` or `tier`. Reproduced 2026-10-22 by test with a resolver written for it; not seen in a real run, where the producer is expected to always set both.
- **Observed symptom:** the row was dropped with no trace, so a dataset with holes looked complete.
- **Cause (established by running the test):** `WriteOnWorker` returned on a blank path.
- **Violated invariant:** work that does not happen says so, with its reason.
- **Recurrence cue:** a `RowUnrouted` Warning, and a `RowsRoutedAgain` carrying the count of rows lost.

Regression: `RoutedJsonlSinkRegressionTests.Rows_without_a_path_are_reported_once_and_counted_when_routing_resumes`.

# JsonlSink bug notes

## A non-finite number cost the whole row and opened a sink incident

- **Trigger:** any event whose payload carries a `double` or `float` that is `NaN` or an infinity (a ratio over a zero duration, an uninitialised measurement), written through `JsonlLineSerializer`. Reproduced 2026-10-22 by test, not seen yet in a real journal.
- **Observed symptom:** `Utf8JsonWriter.WriteNumber` threw `ArgumentException`. `JsonlSink.WriteOnWorker` caught it, closed the stream and rethrew, so the row was lost and the queue opened a `SinkFaulted` incident for a sink whose disk was fine; the next row reopened the stream and closed the incident.
- **Cause (established by running the test):** JSON has no literal for `NaN` or the infinities and the writer refuses them; the serializer passed every `float` and `double` straight to `WriteNumber`.
- **Violated invariant:** one unrepresentable value never costs a row. The serializer is total over any payload an event can carry.
- **Recurrence cue:** a `SinkFaulted` whose `error_type` is `ArgumentException` and whose message mentions "positive and negative infinity", on a sink whose destination is writable.
- **Not a bug, checked:** a lone UTF-16 surrogate in a string. The writer replaces it with U+FFFD without throwing, which `JsonlLineSerializerTests.ALoneSurrogateIsReplacedRatherThanLosingTheRow` pins.

Regression: `JsonlLineSerializerRegressionTests.A_non_finite_double_is_written_as_text_instead_of_losing_the_row` and its float sibling.

# RangeObservableCollection bug notes

## ReplaceAll emptied the list before reading its source

- **Trigger:** `ReplaceAll` given a lazy sequence that reads the collection itself (`collection.Where(...)`), or one that throws midway. Reproduced 2026-10-22 by test.
- **Observed symptom:** the first case left the collection empty (the filter ran after `Clear`); the second left it half-filled with no change notification, so the log window and its data diverged.
- **Cause (established by running the tests):** the loop enumerated the argument after `Items.Clear()`.
- **Violated invariant:** a replacement either happens completely with one Reset, or does not happen.
- **Recurrence cue:** a log window that goes blank after a filter change, or shows fewer rows than its source holds.

Regression: `RangeObservableCollectionRegressionTests` (two tests, one per trigger).

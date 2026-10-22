# DataRootRelocator bug notes

## A second failed undo step was dropped from the rollback error

- **Trigger:** a relocation fails after the target was selected, and then both undo steps fail: restoring the previous root selection and removing the files the copy wrote. Reproduced 2026-10-22 by test with fakes for the copier and the selection.
- **Observed symptom:** the `AggregateException` held the original failure and only the first undo failure. The cleanup failure (files still on disk at the target) was not in it. Confirmed by running the test against the old code: 2 inner exceptions instead of 3.
- **Cause:** the catch blocks shared one variable, `rollbackFailure ??= ex`, which keeps the first exception and discards the next.
- **Violated invariant:** a failure carries its cause, so every step of the undo that failed reaches the one exception the caller reads.
- **Recurrence cue:** an `AggregateException` from a relocation with fewer inner exceptions than the undo steps that failed, for example a selection that was not restored beside a target folder that was left behind.
- **Not covered:** cancellation inside the copy is not tested here; the relocator treats it as any other failure, and no test pins that.

Regression: `DataRootRelocatorTests.A_failed_restore_and_a_failed_cleanup_are_both_reported`.

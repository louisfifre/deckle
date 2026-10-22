# DataRootTree bug notes

## A file removed during the walk aborted the relocation

- **Trigger:** the data tree is relocated while the app still runs, and a file listed by the directory enumeration is removed (log rotation, a temporary file) before its attributes are read. Reproduced 2026-10-22 by test.
- **Observed symptom:** `FileNotFoundException` out of `Copy`, so the whole relocation failed and rolled back for a file that no longer existed.
- **Cause (established by running the test):** `File.GetAttributes` in the walk had no guard, while `MeasureBytes` already tolerated the same vanishing at its own step.
- **Violated invariant:** an entry that is gone has nothing to copy and is not a failure.
- **Recurrence cue:** a failed relocation whose error names a file under `diagnostics` or a `.tmp` path that is absent afterwards.
- **Not covered:** a file that vanishes between the walk and the copy itself still fails as an uncopyable required file; no test pins that case.

Regression: `DataRootTreeRegressionTests.An_entry_that_vanishes_while_the_tree_is_walked_does_not_stop_the_copy`.

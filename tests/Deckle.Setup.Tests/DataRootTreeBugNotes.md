# DataRootTree bug notes

## A file removed during the walk aborted the relocation

- **Trigger:** the data tree is relocated while the app still runs, and a file listed by the directory enumeration is removed (log rotation, a temporary file) before its attributes are read. Reproduced 2026-10-22 by test.
- **Observed symptom:** `FileNotFoundException` out of `Copy`, so the whole relocation failed and rolled back for a file that no longer existed.
- **Cause (established by running the test):** `File.GetAttributes` in the walk had no guard, while `MeasureBytes` already tolerated the same vanishing at its own step.
- **Violated invariant:** an entry that is gone has nothing to copy and is not a failure.
- **Recurrence cue:** a failed relocation whose error names a file under `diagnostics` or a `.tmp` path that is absent afterwards.
- **Not covered:** a file that vanishes between the walk and the copy itself still fails as an uncopyable required file; no test pins that case.

Regression: `DataRootTreeRegressionTests.An_entry_that_vanishes_while_the_tree_is_walked_does_not_stop_the_copy`.

## The error that stopped a relocation did not say why a file was refused

- **Trigger:** a required data file cannot be read for the copy (held open without sharing by another process), the copy is retried once and fails again. Reproduced by test with a file held `FileShare.None`.
- **Observed symptom:** the Error `RelocateFailed` carried "Could not copy required data file 'x'." with no cause. The cause existed only in a Verbose line that the log does not keep. Confirmed by running the test against the old code.
- **Cause:** `Copy` saw a `bool` from the copier, so the exception caught in `TryCopyFile` never reached the throw.
- **Violated invariant:** a failure carries its cause, the step and the error, on one line.
- **Recurrence cue:** a `RelocateFailed` whose `ex_message` names a file but no error type or code.
- **Choice:** the message holds the error type and code, not the OS text, because that text repeats an absolute path. Interpretation, not observed: a reader may later want the OS text, and would then raise the Verbose level.
- **Not covered:** the copier seam used by other tests returns a `bool` and gives no cause, so that path keeps the short message.

Regression: `DataRootTreeRegressionTests.A_required_file_that_cannot_be_copied_names_the_cause_in_the_error`.

# Update handoff bug notes

## Payload replacement guessed when the predecessor had exited

- **Trigger:** The newly extracted update process started while the installed Deckle process was still draining resident services.
- **Observed symptom:** A polling delay could expire too early and expose a manual Retry, or spend the whole delay after the exact predecessor had already exited.
- **Cause:** Handoff inferred process lifetime from repeated folder scans and elapsed time instead of carrying the predecessor PID and waiting on its process handle.
- **Violated invariant:** Update replacement begins only after the exact installed Deckle image that initiated the handoff has signalled exit; a timeout falls back to the ordinary running-process gate and never authorizes copying.
- **Recurrence cue:** Update handoff introduces `Task.Delay`/poll counts, accepts a PID without checking its image path, or bypasses the final folder gate after timeout.
- **Regression coverage:** `UpdatePredecessorTests.Update_handoff_waits_for_the_exact_predecessor_exit_signal` uses explicit gates to prove the wait cannot complete before exit; `Reused_pid_with_another_image_is_not_a_predecessor` pins positive image identity.

## A failed wait for the predecessor escaped the hand-off

- **Trigger:** `Process.WaitForExitAsync` on the predecessor throws something other than a timeout, a `Win32Exception` or an `InvalidOperationException`. Reproduced 2026-10-22 only through the fake process of the test; no real occurrence was observed, so how often the OS does this is not established.
- **Observed symptom:** the exception left `UpdatePredecessor.WaitForExitAsync`, so a wait that is only a courtesy ended the update. Confirmed by running the test against the old code.
- **Cause:** only `TimeoutException` was caught around the wait.
- **Violated invariant:** the hand-off is best effort and the running-process gate stays authoritative, so a wait that fails falls back to that gate, like a timeout, and leaves its cause in the log.
- **Recurrence cue:** an update that stops at the hand-off with `Win32Exception` or `InvalidOperationException` in its trace, or a `PredecessorWaitFailed` Warning followed by a blocked update.
- **Not covered:** other exception types still escape on purpose, such as a cancellation, which no caller passes here.

Regression: `UpdatePredecessorTests.A_wait_that_fails_does_not_stop_the_update_and_is_reported`.

# Update handoff bug notes

## Payload replacement guessed when the predecessor had exited

- **Trigger:** The newly extracted update process started while the installed Deckle process was still draining resident services.
- **Observed symptom:** A polling delay could expire too early and expose a manual Retry, or spend the whole delay after the exact predecessor had already exited.
- **Cause:** Handoff inferred process lifetime from repeated folder scans and elapsed time instead of carrying the predecessor PID and waiting on its process handle.
- **Violated invariant:** Update replacement begins only after the exact installed Deckle image that initiated the handoff has signalled exit; a timeout falls back to the ordinary running-process gate and never authorizes copying.
- **Recurrence cue:** Update handoff introduces `Task.Delay`/poll counts, accepts a PID without checking its image path, or bypasses the final folder gate after timeout.
- **Regression coverage:** `UpdatePredecessorTests.Update_handoff_waits_for_the_exact_predecessor_exit_signal` uses explicit gates to prove the wait cannot complete before exit; `Reused_pid_with_another_image_is_not_a_predecessor` pins positive image identity.

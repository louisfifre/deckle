# Capture incident bug notes

## A capture incident stayed open when the capture loop exited

- **Trigger:** The desktop duplication kept failing to deliver frames (`AcquireFrameIncident` after the failure threshold) or could not be reopened (`DuplicationRecreateAttemptFailed` from the second attempt), and the loop then ended before it recovered: a stop, a cancellation during the recreate backoff, a device lost, a crash.
- **Observed symptom:** The log held a Warning that opened an incident and no line that closed it, so a reader of the file alone could not tell whether the capture was still failing after the loop ended. Established by reading the code, not reproduced against a failing display.
- **Cause:** The only closers were `AcquireFrameRecovered`, `DuplicationRecreated` and the terminal Errors. The incident state lived in locals of the loop and of `TryRecreateDuplication`, and nothing read them in the loop's `finally`.
- **Violated invariant:** A degradation that repeats is one incident closed by one Milestone-tagged event; work that ends with an incident open says so.
- **Recurrence cue:** A new incident kind added to the capture loop with a recovery closer and no entry in `CaptureIncidents.AbandonOpen`; incident state kept in a local that the exit path cannot reach.
- **Regression coverage:** `CaptureIncidentsTests.AnAcquireIncidentOpenWhenTheLoopExitsIsClosedOnce`, `ARecreateIncidentOpenWhenTheLoopExitsIsClosedWithItsAttemptsAndDuration` and `AFlappingDisplayStillOpenWhenTheLoopExitsIsClosedOnce`. The loop and the recreate run against real DXGI and have no seam, so the tests exercise the extracted `CaptureIncidents` tracker; that the loop's `finally` calls `AbandonOpen` is covered by reading only.

## A display that kept being lost and reopened left only Verbose lines

- **Trigger:** The duplication was lost (`DXGI_ERROR_ACCESS_LOST`, or access denied / session disconnected on the secure desktop) several times in a row, for example a flapping monitor, a dock reconnecting or repeated UAC prompts.
- **Observed symptom:** Each loss recreated the duplication and the loop went on, so the log showed nothing while captured colors stuttered. Established by reading the code: the loss lines were Verbose and were not counted.
- **Cause:** A recovered loss is not a failure by itself, and nothing counted the losses that repeat.
- **Violated invariant:** A degradation that repeats is one incident, opened by one Warning and closed by one recovery.
- **Recurrence cue:** A recoverable condition handled by "retry and continue" with only a per-occurrence Verbose line.
- **Regression coverage:** `CaptureIncidentsTests.AFlappingDisplayOpensOneIncidentAndOneRecovery`, `ADisplayLostFewTimesIsNotAnIncident` and `LossesSpreadBeyondTheWindowNeverOpenAnIncident`. The thresholds (4 losses in 30 s, 10 s quiet) are a judgment, not a measured value.

## Swallowed cleanup and wait failures were Verbose

- **Trigger:** Releasing the duplication or waiting on the capture loop threw and the exception was caught.
- **Observed symptom:** `CaptureCleanupFailed` and `CaptureLoopWaitFailed` were written at Verbose, outside the log, although they mean a resource may be leaked or a worker is still running.
- **Cause:** The events were declared for diagnosis of the happy-path teardown and never reconsidered.
- **Violated invariant:** A failure the code swallows is a Warning when work went on.
- **Recurrence cue:** A `catch` that logs only at Verbose around a native release.
- **Regression coverage:** none. Both are raised only from the native teardown, which the suite cannot fail on demand.
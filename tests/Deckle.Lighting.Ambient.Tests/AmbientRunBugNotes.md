# Ambient run bug notes

## A push or frame-processing incident stayed open when the run stopped

- **Trigger:** Ambient lighting ran while the Hue bridge kept refusing pushes (three failures open `PushIncidentOpened`) or while captured frames kept failing for a second (`FrameProcessingIncidentOpened`), and the run was then stopped (user toggle, restart, capture lost) before the incident recovered.
- **Observed symptom:** The journal held a Warning that opened an incident and no line that closed it, so a reader of the file alone could not tell whether the bridge or the capture was still failing after the stop. Established by reading the code, not reproduced at a bridge.
- **Cause:** The only closers were the recoveries (`PushRecovered`, `FrameProcessingRecovered`) and the terminal Errors. `StartAsync` and `Stop` cleared the episode state silently (`ResetPushFailureEpisode`, `ResetFrameProcessingEpisode`), so an incident still open at the end of a run was dropped without a line.
- **Violated invariant:** A degradation that repeats is one incident closed by one journal-tagged event; a run that ends with an incident open says so.
- **Recurrence cue:** A new incident kind added to the engine with a recovery closer and no entry in `AbandonOpenIncidents`; an episode reset that runs without checking whether the incident flag is still set.
- **Regression coverage:** `AmbientRunJournalTests.StoppingWithAnOpenPushIncidentClosesItOnce` opens a push incident, stops twice and asserts a single `PipelineIncidentAbandoned`; `ARecoveredPushIncidentHasNothingLeftToClose` and `FailuresBelowTheIncidentThresholdHaveNothingToClose` assert it stays silent otherwise. The frame-processing branch has no test: its incident opens after one second of wall-clock failures and the engine has no clock seam, so it is covered by reading only.

## An upstream abort that lost the race left no trace of its cause

- **Trigger:** A capture loss, a push rejection or a frame-processing failure reached `AbortStartOrStop` while the engine was already Stopping or Off, or while an earlier abort of the same run was pending.
- **Observed symptom:** The consequence line of the later cause (`CaptureLost`, `PushRejected`, `FrameProcessingFailed`) was never written. Established by reading the code; the first abort of a run still writes its own line, so this is a loss of the second cause only.
- **Cause:** `AbortStartOrStop` returned before running the cause's log action, by design, so a run reports one consequence.
- **Violated invariant:** A cause that is set aside is still findable.
- **Recurrence cue:** An early return in an abort or cancel path that skips the caller's log action without leaving any line.
- **Regression coverage:** none yet. The new `PipelineAbortIgnoredDetail` (Verbose) names the reason and why it was ignored; it is reached only through a live capture, which the suite cannot start.
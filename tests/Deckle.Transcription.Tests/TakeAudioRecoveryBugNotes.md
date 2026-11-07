# Take audio recovery bug notes

Found by reading the code of the v0.74.0 safety net, not from a real log. Causes (a) and (c) are confirmed by tests that failed before the fix or by construction; (b) is reproduced by a test and not fixed.

## A recovery marker left by a crash blocked the audio recovery for good

- Trigger: the app dies while a recovery run is going; `take-audio.attempted` stays on disk. A later take then loses its text and leaves its own `take-audio.dat`.
- Symptom: at every launch the recovery logs `previous_attempt_crashed` and does nothing; the newer take's audio is never transcribed. Nothing in the UI says so.
- Cause: `TakeAudioRecovery.TryRecoverAsync` returned as soon as the marker existed, with no limit in time. Inferred, not seen in real use.
- Violated invariant: the marker protects against a crash loop, not against every later take.
- Fix: a marker older than 24 h is deleted with a Warning milestone (`TakeAudioAttemptExpired`), then the newer audio runs. `MarkAttempted` stamps the marker with the time of the attempt, because a file move keeps the write time of the last audio record and old audio would otherwise look expired at once.
- Recurrence cue: `previous_attempt_crashed` repeated across days in the log.
- Guard: `TakeAudioRecoveryTests.AMarkerOlderThanADayExpiresAndTheNewerTakeIsRecovered`, `AnExpiredMarkerWarnsWithItsAgeAndNoContent`. A marker younger than a day still blocks (`APreviousAttemptThatNeverFinishedIsNotRetried`).
- Interpretation: the dropped marker's audio is lost (it crashed the app once, a day ago). 24 h is a default for the maintainer to review.

## A failed audio recovery ended on the "recovered dictation" success

- Trigger: at launch the audio run fails or is skipped, and a text copy of the take exists.
- Symptom: the text copy is restored and the HUD says "Recovered dictation copied": the person reads a partial text as the whole take.
- Cause: every non-recovered result fell through to `RestoreInterruptedDictation`, which always announces success.
- Violated invariant: a failure of the audio recovery is never hidden by a success message.
- Fix: `TakeAudioRecoveryResult.Failed`; the app restores the text silently and shows the failure message, saying whether only the saved text reached the clipboard. An audio without speech and a delivered run are not failures.
- Recurrence cue: a new early return in `TryRecoverAsync` that returns `default` for audio that is still waiting.
- Guard: `TakeAudioRecoveryTests.AFailedRunReportsTheFailure`, `AThrowingRunAndAPreviousCrashReportTheFailure`, `ADeliveredRunAndAnAudioWithoutSpeechAreNotFailures`. The HUD wiring in `App.Startup.DictationRecovery.cs` has no test (the App class is not unit-tested); it was checked by reading only.

## A partial take deletes the audio of its failed part (open)

- Trigger: a live take where one utterance fails in the backend and another gives text.
- Symptom: the partial text is delivered, then the audio is deleted; the failed words cannot be recovered.
- Cause: `DiscardsTakeAudio` looks only at the outcome (`pasted`, `clipboard_only`), not at the take having been incomplete.
- Violated invariant: the audio goes only when nothing is left to recover.
- Fix: none yet. Keeping the audio in the single slot would last only until the next take, whose `TryBegin` replaces the file. A real fix needs a second slot, a size cap and a rule for a take whose text was already pasted.
- Guard: `TakeAudioRecoveryEngineTests.APartialTakeKeepsItsAudioForTheFailedPart`, skipped, with the reproduction.

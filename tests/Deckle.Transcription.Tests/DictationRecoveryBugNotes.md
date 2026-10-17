# Dictation recovery bug notes

## The recovery file was cleared by the wrong event

Found in review on 2026-10-18, before the recovery work merged. Never shipped.

- **Trigger.** Two endings of a live take. First, the microphone drops after
  some utterances were already transcribed, or the app quits or restarts in
  the middle of a take. Second, the raw copy reaches the clipboard and then the
  rewrite or the paste throws.
- **Symptom.** In the first case the recovery file was deleted, so the words
  already transcribed were lost. In the second case the file was kept, so the
  next launch replaced the clipboard with that old text. Both were read in the
  code and reproduced by the tests below. Neither was seen on screen.
- **Cause.** `WorkerRun` decided from the take's result. A null production
  deleted the file as "nothing to recover", but a null production also covers
  an abort that had text. A finalize that threw went to the crash handler,
  which kept the file even though the clipboard already held the text.
- **Violated invariant.** The file is the only copy of the take's text until
  the clipboard holds that text, and from then on it must not exist.
  `FinalizeTranscription` now clears it right after the raw copy lands, and
  no other ending clears it.
- **Recurrence cue.** A `ClearRecovery` call placed anywhere other than right
  after the raw copy, or a new ending of a take that deletes the file on the
  assumption that it holds nothing.
- **Guarded by.** `LiveTranscriptionPipelineTests`:
  `RecoveryFileSurvivesAMicrophoneErrorAfterTranscribedUtterances` and
  `RecoveryFileIsGoneWhenDeliveryFailsAfterTheRawCopy`. The quit in the
  middle of a take has no test. It takes the same null path as the microphone
  error, which is inferred from the code and not exercised.

# Dictation end-of-take capture bug notes

## The last words before the stop press were cut off

Found by reading the code on 2026-10-21, after reports that the end of a
dictation was often missing. Not reproduced on a microphone.

- **Trigger.** Pressing the hotkey to stop while still finishing the last word,
  which is the normal way to stop.
- **Symptom.** The final word or two were missing from the transcript. Reported
  by the maintainer; the link to this cause is inferred from the code.
- **Cause.** `RequestToggle` cancelled the capture token in the same call as the
  Recording to Stopping transition. The waveIn loop then stopped within one
  50 ms buffer of the press, so nothing spoken at or after the press was
  recorded, and the word still being pronounced as the key went down was clipped.
- **Violated invariant.** The audio of a take runs until a moment after the
  user's stop gesture, not up to it. A gesture marks the end of the intention,
  and the speech it follows is still in flight.
- **Recurrence cue.** The stop branch of `RequestToggle` calling `Cancel()` on
  the record source instead of `CancelAfter(CAPTURE_TAIL_AFTER_STOP_MS)`, or a
  new stop path that cancels the capture token directly. `Dispose` is the one
  path that must cancel at once.
- **Guarded by.** `LiveTranscriptionPipelineTests`:
  `CaptureKeepsRunningForAShortTailAfterTheStopPress`. The 400 ms value is a
  judgement, not a measurement: it is not checked against real speech, and the
  test only asserts a loose lower bound of 300 ms.

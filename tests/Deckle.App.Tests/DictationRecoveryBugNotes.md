# Dictation recovery observability bug notes

## A dictation put back on the clipboard left no line in the log

- Trigger: Deckle starts after a run that ended with a dictation in flight, and the recovered text reaches the clipboard.
- Symptom: the log held nothing about it. `DictationRecovered` was Informational without the Milestone tag, so only the trace kept it, while the three other ends of a recovery (`DictationRecoveryUnreadable`, `DictationRecoveryClipboardFailed`, `DictationRecoveryKept`) are Warnings the log keeps. Found by reading the declared events when `MilestoneClosersTests` first ran on `Deckle.App`, not from a real log.
- Cause: the event predates the rule that every event named `*Recovered` travels in the log, and `Deckle.App` had no test suite to apply that rule until v0.68.1.
- Violated invariant: an end that changes what the person has at hand, here the clipboard, is told in the log, like the other ends of the same work.
- Fix: `DictationRecovered` carries the Milestone tag. `DictationRecoveryFound` stays out of the log: the outcome alone tells the story, and every outcome now travels.
- Recurrence cue: a recovery outcome event of `DeckleAppSource` added at Informational without the Milestone tag; a person who finds unexpected text on the clipboard after a restart and a log that does not say why.
- Guard: `MilestoneClosersTests.DeckleAppSourceRecoveryClosersTravelInTheLog`. It guards the name, not the meaning: an outcome named otherwise is not covered.
- Interpretation: `DictationRecovered` is not the closer of an incident in the sense the guard was written for (no Warning opens it); the tag is kept because its meaning, an end of work that changed the machine, calls for the log as well.

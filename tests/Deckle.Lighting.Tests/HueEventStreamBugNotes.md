# Hue event stream bug notes

## An event-stream incident stayed open when monitoring stopped

- **Trigger:** The Hue event stream lost its connection long enough to open an incident (`EventStreamIncident`), and its subscriber then stopped (bridge change, Ambient stop, shutdown) before the stream recovered.
- **Observed symptom:** The journal held an incident opening with no closer: a reader of the file alone could not tell whether the bridge was still unreachable or monitoring had simply ended. Established by reading the code, not reproduced at a bridge.
- **Cause:** The only closer of the incident, `EventStreamRecovered`, fires on a successful reconnect. The stream loop's `finally` wrote `EventStreamStopped` and discarded the episode state, so an open incident was dropped without a closer.
- **Violated invariant:** A degradation that repeats is one incident closed by one journal-tagged event; an end that nothing declared reads as abandoned.
- **Recurrence cue:** A loop's exit path that logs "stopped" without asking the episode tracker whether an incident is still open; a new incident kind added with a single recovery closer and no abandon path.
- **Regression coverage:** `HueEventStreamEpisodeTests.StoppingWhileTheIncidentIsOpenClosesItOnce` asserts the stop reports the open incident once and resets the state; `StoppingDuringABriefLossHasNothingToClose` asserts that a loss too short to have opened an incident writes nothing. `JournalClosersTests` guards that `EventStreamIncidentAbandoned` travels in the journal.

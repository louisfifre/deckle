# Whisp event id bug notes

## Two branches gave the same event id to different events, and the whole provider went silent

- Trigger: main added `EvtShutdownDrainStarted = 255` and `EvtShutdownDrainFinished = 256` (quit finishes the work in progress). Branch `take-audio-net` added `EvtTakeAudioAttemptExpired = 255` and `EvtCorpusRecordSkipped = 256`. Merging main into the branch on 2026-11-07 raised no conflict, because the constants sit on different lines of `DeckleWhispSource.cs`.
- Symptom: 33 tests of `Deckle.Transcription.Tests` failed at once. Most of them observe unrelated events (take start check, pipeline milestones, microphone incident). An `EventListener` received only one `EventSourceMessage` with id 0 from `Deckle-Whisp`, never the expected event. Observed in the test run, not in the app; the merge never reached main.
- Cause: an `EventSource` whose `[Event]` attributes share an id cannot build its event table. The runtime then reports one `EventSourceMessage` and writes none of the provider's events. Confirmed: moving the branch's two events to 258 and 259 made the 33 tests pass again.
- Violated invariant: every `[Event]` method of an `EventSource` has its own id, across all the partial files of the source.
- Recurrence cue: many observability tests of one provider failing together, with `EventSourceMessage` as the only event in the collection; or a merge that adds `public const int Evt... = N;` on both sides.
- Guard: `DeckleWhispSourceEventIdTests.EveryWhispEventHasItsOwnId` reads the `[Event]` attributes by reflection and lists any shared id. It failed with the duplicate and passes with the fix. `EventSource.GenerateManifest` is not used as a guard: even without options it rejects the keywords Deckle declares in `Deckle.Diagnostics.Keywords`, which the runtime accepts.
- Uncertain: other providers in the solution have no such guard yet; whether they carry the same defect was checked separately on 2026-11-07, not by a test.

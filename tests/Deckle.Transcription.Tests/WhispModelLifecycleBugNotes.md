# Whisp model lifecycle observability bug notes

## A model load and a warmup had no identifier pairing their start and end

- Trigger: the first take or file batch after start, or after the idle unload, loads the model and primes the backend.
- Symptom: the journal could not say which `Loading model` a `Model loaded` or a failure belonged to, nor which warmup ended how: `ModelLoading`, `ModelLoaded` and `ModelUnavailable` carried no identifier or duration, and `WarmupStart`, `WarmupComplete` and `WarmupCancelled` were Informational without the Journal tag, so the always-on journal dropped them.
- Cause: the events were written as milestones, not as the start and end of lasting work.
- Violated invariant: lasting work writes a start and an end that share one identifier, both in the journal, and the end carries its duration.
- Fix: `ModelLoading`/`ModelLoaded` and `WarmupStart`/`WarmupComplete`/`WarmupCancelled`/`WarmupFailed` carry a `work_id` (`WorkId.Next()`); the warmup pair travels in the journal and the ends carry `elapsed_ms`, the load end carries `load_ms` and `device`. A warmup that cannot load the model ends in `WarmupFailed` (Warning) with the id of its start.
- Recurrence cue: a `ModelLoading` or `WarmupStart` line with no line of the same `work_id` after it.
- Guard: `WhispModelLifecycleTests.AColdLoadAndItsWarmupEachPairTheirStartAndEndInTheJournal`, `AWarmupThatCannotLoadTheModelEndsInAWarningWithTheStartsId`.
- Interpretation: the backend (`DeckleWhisperSource`) keeps its own journal lines for the same load (`ModelLoadStarting`, `ModelOnGpu`, `ModelOnCpu`). They are not paired by id with the engine's pair, because that would put an identifier in the `IAsrBackend` contract. The journal therefore shows two start lines per load; this is a choice, not an oversight.

## An unavailable model was an incident with no id, and its recovery named no incident

- Trigger: the model file is missing or unusable, and each take or file batch retries the load until it works.
- Symptom: the Error `ModelUnavailable` and the later `ModelRecovered` could not be matched in the file.
- Cause: the incident was a flag; nothing was kept of the load that opened it.
- Fix: the engine keeps the `work_id` of the load that opened the incident. While the incident is open, a retry writes the Verbose `ModelLoadRetrying` (and `ModelLoadRetryFailed`) instead of `ModelLoading` and stays out of the journal; `ModelRecovered` repeats the opening load's id.
- Recurrence cue: a `ModelRecovered` whose `work_id` matches no `ModelUnavailable`, or one `ModelLoading` per retry while the model stays missing.
- Guard: `WhispModelLifecycleTests.AModelThatStaysUnavailableIsOneIncidentAndItsRecoveryRepeatsTheIncidentId`.

## A failed idle unload was an Error although transcription went on

- Trigger: `UnloadModel` throws when the idle timer fires.
- Symptom: an Error line for a failure after which the app keeps working.
- Cause: the level was Error.
- Fix: `ModelIdleUnloadFailed` is a Warning (the work went on degraded: the model stays loaded).
- Guard: `WhispModelLifecycleTests.AFailedIdleUnloadIsADegradationNotAStop`. It calls the provider method; the timer callback has no seam.
- Interpretation: the event still carries the raw exception message (the personal-data finding on payloads is handled separately).

## A fallback did not say what was asked and what was obtained

- Trigger: the configured model file is missing and an installed one is used, or the model path variable points at a file that is not an existing absolute path.
- Symptom: the Warnings `ModelFallback` and `ModelPathEnvIgnored` carried no payload. The two file names were in separate Verbose detail events, so the default journal said that a fallback happened, not between which files.
- Cause: the names were split from the Warning into a detail event.
- Fix: `ModelFallback(configured, installed)` names both files. `ModelPathEnvIgnored(fallback_file)` names the file used and a fixed reason; the variable's value (a user path) stays in the Verbose `ModelPathEnvIgnoredDetail`. `ModelFallbackDetail` (id 166) is retired.
- Guard: `WhispModelLifecycleTests.AFallbackNamesWhatWasAskedAndWhatWasObtained`, `AnIgnoredModelPathNamesTheFileUsedAndKeepsTheVariablesValueInTheDetail`. Both call the provider methods: the resolver that chooses the fallback is internal to the Whisper backend, and its load reaches the native library, so no seam exercises the whole path.

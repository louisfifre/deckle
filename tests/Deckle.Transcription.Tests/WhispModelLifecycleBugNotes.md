# Whisp model lifecycle observability bug notes

## A model load and a warmup had no identifier pairing their start and end

- Trigger: the first take or file batch after start, or after the idle unload, loads the model and primes the backend.
- Symptom: the log could not say which `Loading model` a `Model loaded` or a failure belonged to, nor which warmup ended how: `ModelLoading`, `ModelLoaded` and `ModelUnavailable` carried no identifier or duration, and `WarmupStart`, `WarmupComplete` and `WarmupCancelled` were Informational without the Milestone tag, so the always-on log dropped them.
- Cause: the events were written as milestones, not as the start and end of lasting work.
- Violated invariant: lasting work writes a start and an end that share one identifier, both in the log, and the end carries its duration.
- Fix: `ModelLoading`/`ModelLoaded` and `WarmupStart`/`WarmupComplete`/`WarmupCancelled`/`WarmupFailed` carry a `work_id` (`WorkId.Next()`); the warmup pair travels in the log and the ends carry `elapsed_ms`, the load end carries `load_ms` and `device`. A warmup that cannot load the model ends in `WarmupFailed` (Warning) with the id of its start.
- Recurrence cue: a `ModelLoading` or `WarmupStart` line with no line of the same `work_id` after it.
- Guard: `WhispModelLifecycleTests.AColdLoadAndItsWarmupEachPairTheirStartAndEndInTheLog`, `AWarmupThatCannotLoadTheModelEndsInAWarningWithTheStartsId`.
- Interpretation: the backend (`DeckleWhisperSource`) keeps its own log lines for the same load (`ModelLoadStarting`, `ModelOnGpu`, `ModelOnCpu`). They are not paired by id with the engine's pair, because that would put an identifier in the `IAsrBackend` contract. The log therefore shows two start lines per load; this is a choice, not an oversight.

## A failed model load ended as an exception when the settings page's strings were absent

- Trigger: the model cannot be loaded in a process whose resources hold no map for the library that carries the settings page's strings. Seen in the engine's tests on 2026-11-11, the day those strings moved from `Deckle.Transcription` to `Deckle.Transcription.Views`: the test host compiles no `.resw` any more.
- Symptom: the warmup ended in `WarmupFailed` with the reason `prime_exception` instead of `model_load_failed`.
- Cause: the message for a failed load names the settings page through `Loc.GetFrom`. `Loc.TryGetFrom` built the library's `ResourceLoader` outside its `try`, and that constructor throws `COMException: ResourceMap Not Found` when the map is absent. Established from the exception the test reported.
- Violated invariant: a missing string is a miss (a marker in Debug, empty in Release), never an exception, whether the key or its whole map is absent. The trap is one line: see the comment in `Loc.TryGetFrom` (`src/Deckle.Localization/Loc.cs`).
- Fix: the loader is built inside the `try`; a failed construction is not cached.
- Recurrence cue: a `prime_exception` on a load that failed for an ordinary reason, or any `ResourceMap Not Found` crossing `Loc`.
- Guard: `WhispModelLifecycleTests.AWarmupThatCannotLoadTheModelEndsInAWarningWithTheStartsId`. It guards this only while the test project references no project that compiles the page's strings. `Loc` has no suite of its own, so no test calls `Loc.GetFrom` on an absent map directly.
- Interpretation: the installed app was not affected, since it ships the Views assembly and its map. Not observed there, inferred from how the map is built.

## An unavailable model was an incident with no id, and its recovery named no incident

- Trigger: the model file is missing or unusable, and each take or file batch retries the load until it works.
- Symptom: the Error `ModelUnavailable` and the later `ModelRecovered` could not be matched in the file.
- Cause: the incident was a flag; nothing was kept of the load that opened it.
- Fix: the engine keeps the `work_id` of the load that opened the incident. While the incident is open, a retry writes the Verbose `ModelLoadRetrying` (and `ModelLoadRetryFailed`) instead of `ModelLoading` and stays out of the log; `ModelRecovered` repeats the opening load's id.
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
- Symptom: the Warnings `ModelFallback` and `ModelPathEnvIgnored` carried no payload. The two file names were in separate Verbose detail events, so the default log said that a fallback happened, not between which files.
- Cause: the names were split from the Warning into a detail event.
- Fix: `ModelFallback(configured, installed)` names both files. `ModelPathEnvIgnored(fallback_file)` names the file used and a fixed reason; the variable's value (a user path) stays in the Verbose `ModelPathEnvIgnoredDetail`. `ModelFallbackDetail` (id 166) is retired.
- Guard: `WhispModelLifecycleTests.AFallbackNamesWhatWasAskedAndWhatWasObtained`, `AnIgnoredModelPathNamesTheFileUsedAndKeepsTheVariablesValueInTheDetail`. Both call the provider methods: the resolver that chooses the fallback is internal to the Whisper backend, and its load reaches the native library, so no seam exercises the whole path.

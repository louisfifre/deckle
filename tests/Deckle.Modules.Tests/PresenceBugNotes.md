# Presence observability bug notes

## A presence file that could not be used was a Warning with no cause, and one that lacked its list was silent

- Trigger: `presence.json` is corrupt or unreadable, or it is valid JSON without the `present` list.
- Symptom: the corrupt file produced a Warning that said only that the file could not be read; the error type, the exception message and the absolute path were in a separate Verbose event. A valid file without `present` produced nothing at all. In both cases every module then counts as present, a choice the person never made, and the journal could not say why.
- Cause: the cause was split into a detail event, and the missing-list branch returned `null` without an event.
- Violated invariant: a fallback is a Warning that names what was asked and what was obtained, with its cause on one line, and no absolute path under the user's profile.
- Fix: `PresenceLoadFailed(reason)` is a Warning in the journal. `reason` is the exception type, or `present_list_missing` (`PresenceLoadFailureReasons`). The detail event (id 7) is retired, so the exception message and the path are no longer written. `PresenceSavedDetail` lost its `path` payload for the same reason.
- Recurrence cue: an install where modules are all present although the person chose some, with no `presence file could not be used` line in the journal.
- Guard: `PresenceObservabilityTests.ACorruptPresenceFileIsAWarningInTheJournalNamingTheErrorTypeAndNoPath`, `AValidFileWithoutAPresentListIsAWarningNotASilentNoChoice`.
- Not a defect: `PresenceFile.SaveTo` lets its exceptions propagate to the caller, which is how the module selector learns that the save failed; swallowing them to log would hide the failure from it.

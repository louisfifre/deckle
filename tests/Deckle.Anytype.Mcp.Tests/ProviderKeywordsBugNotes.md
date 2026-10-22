# Anytype MCP provider keyword bug notes

The cause, the fix and the recurrence cue are the same as for the Anytype provider; see `ProviderKeywordsBugNotes.md` in `Deckle.Anytype.Tests`.

- Trigger: someone filters the log on the transverse `Lifecycle` keyword.
- Symptom: `RequestRejected`, `HostInternalLog`, `HostInternalLogRepeated` and `RequestRejectionsEnded` carried only the module bit (0x400), so the filter missed them while it found `HostStarted` and `HostStopped`.
- Guard: `ProviderKeywordsTests.EveryHostLifecycleEventCarriesTheTransverseLifecycleKeyword`.